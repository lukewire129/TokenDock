using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock.Services;

public sealed class OpenAiUsageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public OpenAiUsageService(HttpClient httpClient, string baseUrl = "https://chatgpt.com/backend-api")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = NormalizeBaseUri(baseUrl);
    }

    public async Task<OpenAiUsageSnapshot> GetUsageAsync(
        string accessToken,
        string? chatGptAccountId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(accessToken));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUsageUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("codex-usage-client");

        if (!string.IsNullOrWhiteSpace(chatGptAccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", chatGptAccountId);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to fetch OpenAI usage. Status={(int)response.StatusCode}, Body={content}",
                null,
                response.StatusCode);
        }

        var payload = JsonSerializer.Deserialize<UsagePayload>(content, JsonOptions)
            ?? throw new InvalidOperationException("OpenAI usage response was empty.");

        return MapUsage(payload);
    }

    private Uri BuildUsageUri()
    {
        var path = _baseUri.AbsoluteUri.Contains("/backend-api", StringComparison.OrdinalIgnoreCase)
            ? "wham/usage"
            : "api/codex/usage";

        return new Uri(_baseUri, path);
    }

    private static OpenAiUsageSnapshot MapUsage(UsagePayload payload)
    {
        var primary = MapWindow(payload.RateLimit?.PrimaryWindow);
        var secondary = MapWindow(payload.RateLimit?.SecondaryWindow);

        return new OpenAiUsageSnapshot(
            PlanType: payload.PlanType,
            FiveHourLimit: primary,
            WeeklyLimit: secondary,
            Credits: MapCredits(payload.Credits),
            RateLimitReachedType: payload.RateLimitReachedType?.Kind,
            AdditionalLimits: payload.AdditionalRateLimits?
                .Select(limit => new OpenAiAdditionalUsageLimit(
                    LimitId: limit.MeteredFeature,
                    LimitName: limit.LimitName,
                    Primary: MapWindow(limit.RateLimit?.PrimaryWindow),
                    Secondary: MapWindow(limit.RateLimit?.SecondaryWindow)))
                .ToArray() ?? Array.Empty<OpenAiAdditionalUsageLimit>());
    }

    private static OpenAiUsageWindow? MapWindow(RateLimitWindowPayload? window)
    {
        if (window is null)
        {
            return null;
        }

        return new OpenAiUsageWindow(
            UsedPercent: window.UsedPercent,
            WindowDuration: window.LimitWindowSeconds > 0
                ? TimeSpan.FromSeconds(window.LimitWindowSeconds)
                : null,
            ResetsAt: DateTimeOffset.FromUnixTimeSeconds(window.ResetAt));
    }

    private static OpenAiCreditsSnapshot? MapCredits(CreditStatusPayload? credits)
    {
        if (credits is null)
        {
            return null;
        }

        return new OpenAiCreditsSnapshot(
            HasCredits: credits.HasCredits,
            Unlimited: credits.Unlimited,
            Balance: credits.Balance);
    }

    private static Uri NormalizeBaseUri(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        }

        var normalized = baseUrl.Trim().TrimEnd('/');
        if ((normalized.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("https://chat.openai.com", StringComparison.OrdinalIgnoreCase))
            && !normalized.Contains("/backend-api", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/backend-api";
        }

        return new Uri(normalized + "/", UriKind.Absolute);
    }

    private sealed record UsagePayload(
        [property: JsonPropertyName("plan_type")] string PlanType,
        [property: JsonPropertyName("rate_limit")] RateLimitStatusPayload? RateLimit,
        [property: JsonPropertyName("additional_rate_limits")] IReadOnlyList<AdditionalRateLimitPayload>? AdditionalRateLimits,
        [property: JsonPropertyName("credits")] CreditStatusPayload? Credits,
        [property: JsonPropertyName("rate_limit_reached_type")] RateLimitReachedTypePayload? RateLimitReachedType);

    private sealed record RateLimitStatusPayload(
        [property: JsonPropertyName("primary_window")] RateLimitWindowPayload? PrimaryWindow,
        [property: JsonPropertyName("secondary_window")] RateLimitWindowPayload? SecondaryWindow);

    private sealed record RateLimitWindowPayload(
        [property: JsonPropertyName("used_percent")] int UsedPercent,
        [property: JsonPropertyName("limit_window_seconds")] int LimitWindowSeconds,
        [property: JsonPropertyName("reset_at")] long ResetAt);

    private sealed record AdditionalRateLimitPayload(
        [property: JsonPropertyName("limit_name")] string LimitName,
        [property: JsonPropertyName("metered_feature")] string MeteredFeature,
        [property: JsonPropertyName("rate_limit")] RateLimitStatusPayload? RateLimit);

    private sealed record CreditStatusPayload(
        [property: JsonPropertyName("has_credits")] bool HasCredits,
        [property: JsonPropertyName("unlimited")] bool Unlimited,
        [property: JsonPropertyName("balance")] string? Balance);

    private sealed record RateLimitReachedTypePayload(
        [property: JsonPropertyName("kind")] string Kind);
}

public sealed record OpenAiUsageSnapshot(
    string PlanType,
    OpenAiUsageWindow? FiveHourLimit,
    OpenAiUsageWindow? WeeklyLimit,
    OpenAiCreditsSnapshot? Credits,
    string? RateLimitReachedType,
    IReadOnlyList<OpenAiAdditionalUsageLimit> AdditionalLimits);

public sealed record OpenAiUsageWindow(
    int UsedPercent,
    TimeSpan? WindowDuration,
    DateTimeOffset ResetsAt);

public sealed record OpenAiCreditsSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record OpenAiAdditionalUsageLimit(
    string LimitId,
    string LimitName,
    OpenAiUsageWindow? Primary,
    OpenAiUsageWindow? Secondary);
