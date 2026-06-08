using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock.Services;

public sealed class OpenAiBrowserLoginService
{
    private const string Issuer = "https://auth.openai.com";
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const int DefaultPort = 1455;
    private const int FallbackPort = 1457;

    private readonly HttpClient _httpClient;

    public OpenAiBrowserLoginService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CodexAuthTokens> LoginAsync(CancellationToken cancellationToken = default)
    {
        var port = TryBindPort(DefaultPort, out var listener)
            ? DefaultPort
            : TryBindPort(FallbackPort, out listener)
                ? FallbackPort
                : throw new InvalidOperationException("Login callback port is unavailable.");

        try
        {
            var pkce = CreatePkce();
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var redirectUri = $"http://localhost:{port}/auth/callback";
            var authUrl = BuildAuthorizeUrl(redirectUri, pkce.Challenge, state);

            OpenBrowser(authUrl);
            var code = await WaitForCallbackAsync(listener, state, cancellationToken).ConfigureAwait(false);
            var tokens = await ExchangeCodeForTokensAsync(code, redirectUri, pkce.Verifier, cancellationToken).ConfigureAwait(false);

            return new CodexAuthTokens(
                AccessToken: tokens.AccessToken,
                ChatGptAccountId: TryGetChatGptAccountId(tokens.IdToken),
                RefreshToken: tokens.RefreshToken,
                ExpiresAt: TryGetExpiresAt(tokens.AccessToken));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool TryBindPort(int port, out TcpListener listener)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return true;
        }
        catch
        {
            listener.Stop();
            return false;
        }
    }

    private static async Task<string> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var client = await listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        var request = Encoding.UTF8.GetString(buffer, 0, read);
        var requestLine = request.Split("\r\n", StringSplitOptions.None)[0];
        var path = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        var uri = new Uri($"http://localhost{path}");
        var query = ParseQuery(uri.Query);

        if (!query.TryGetValue("state", out var state) || state != expectedState)
        {
            await WriteResponseAsync(stream, "Login failed", "Invalid login state.", linked.Token).ConfigureAwait(false);
            throw new InvalidOperationException("Login callback state did not match.");
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            var error = query.TryGetValue("error", out var value) ? value : "missing authorization code";
            await WriteResponseAsync(stream, "Login failed", WebUtility.HtmlEncode(error), linked.Token).ConfigureAwait(false);
            throw new InvalidOperationException($"Login callback did not include an authorization code: {error}");
        }

        await WriteResponseAsync(stream, "Login complete", "You can return to TokenDock.", linked.Token).ConfigureAwait(false);
        return code;
    }

    private async Task<TokenResponse> ExchangeCodeForTokensAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = codeVerifier
        });

        using var response = await _httpClient.PostAsync($"{Issuer}/oauth/token", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Token exchange failed. Status={(int)response.StatusCode}, Body={body}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Token exchange response was empty.");
    }

    private static string BuildAuthorizeUrl(string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "openid profile email offline_access api.connectors.read api.connectors.invoke",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = "codex_cli_rs"
        };

        var builder = new StringBuilder($"{Issuer}/oauth/authorize?");
        var first = true;
        foreach (var item in query)
        {
            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(WebUtility.UrlEncode(item.Key));
            builder.Append('=');
            builder.Append(WebUtility.UrlEncode(item.Value));
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            result[WebUtility.UrlDecode(pair[0])] = pair.Length == 2 ? WebUtility.UrlDecode(pair[1]) : string.Empty;
        }

        return result;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string title, string message, CancellationToken cancellationToken)
    {
        var html = $"<html><body style='font-family:Segoe UI,sans-serif;padding:32px'><h2>{title}</h2><p>{message}</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static PkcePair CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new PkcePair(verifier, challenge);
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string? TryGetChatGptAccountId(string idToken)
    {
        return TryGetJwtPayload(idToken)?
            .GetPropertyOrNull("https://api.openai.com/auth")?
            .GetPropertyOrNull("chatgpt_account_id")?
            .GetString();
    }

    private static DateTimeOffset? TryGetExpiresAt(string jwt)
    {
        var payload = TryGetJwtPayload(jwt);
        if (payload is null || !payload.Value.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static JsonElement? TryGetJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record PkcePair(string Verifier, string Challenge);

    private sealed record TokenResponse(
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken);
}
