using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock.Services;

public sealed class CodexAuthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public CodexAuthStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenDock",
            "auth.json"))
    {
    }

    public CodexAuthStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<CodexAuthTokens?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_filePath);
        var payload = await JsonSerializer.DeserializeAsync<AuthFilePayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            return null;
        }

        return new CodexAuthTokens(
            AccessToken: Unprotect(payload.AccessToken),
            ChatGptAccountId: UnprotectOptional(payload.ChatGptAccountId),
            RefreshToken: UnprotectOptional(payload.RefreshToken),
            ExpiresAt: payload.ExpiresAt);
    }

    public async Task SaveAsync(CodexAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(tokens));
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new AuthFilePayload(
            Version: 1,
            AccessToken: Protect(tokens.AccessToken),
            ChatGptAccountId: ProtectOptional(tokens.ChatGptAccountId),
            RefreshToken: ProtectOptional(tokens.RefreshToken),
            ExpiresAt: tokens.ExpiresAt,
            UpdatedAt: DateTimeOffset.UtcNow);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }

    private static string Protect(string value)
    {
        EnsureWindowsDpapiSupported();

        var bytes = Encoding.UTF8.GetBytes(value);
#pragma warning disable CA1416
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string value)
    {
        EnsureWindowsDpapiSupported();

        var encrypted = Convert.FromBase64String(value);
#pragma warning disable CA1416
        var bytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
        return Encoding.UTF8.GetString(bytes);
    }

    private static void EnsureWindowsDpapiSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Codex auth encryption uses Windows DPAPI.");
        }
    }

    private static string? ProtectOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Protect(value);
    }

    private static string? UnprotectOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Unprotect(value);
    }

    private sealed record AuthFilePayload(
        int Version,
        string AccessToken,
        string? ChatGptAccountId,
        string? RefreshToken,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset UpdatedAt);
}

public sealed record CodexAuthTokens(
    string AccessToken,
    string? ChatGptAccountId = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null);
