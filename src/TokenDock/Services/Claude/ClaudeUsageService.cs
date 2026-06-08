using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock.Services;

public sealed class ClaudeTokenExpiredException : InvalidOperationException
{
    public ClaudeTokenExpiredException(string message)
        : base(message)
    {
    }
}

public sealed class ClaudeUsageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);
    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TokenDock",
        "claude-usage-cache.json");

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly string? _claudeCliPath;
    private ClaudeUsageSnapshot? _cachedSnapshot;
    private DateTime _cacheTimestamp = DateTime.MinValue;

    public ClaudeUsageService(HttpClient httpClient, string? claudeCliPath = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _claudeCliPath = claudeCliPath;
        LoadCacheFromDisk();
    }

    public async Task<ClaudeUsageSnapshot> GetUsageSnapshotAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedSnapshot is not null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
        {
            return _cachedSnapshot;
        }

        await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cachedSnapshot is not null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
            {
                return _cachedSnapshot;
            }

            var result = await GetUsageAutoAsync(cancellationToken).ConfigureAwait(false);
            _cachedSnapshot = result;
            _cacheTimestamp = DateTime.UtcNow;
            SaveCacheToDisk(result);
            return result;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public async Task<string> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var credPath = GetCredentialsPath();

        if (!File.Exists(credPath))
        {
            sb.AppendLine("credentials 파일 없음");
            sb.AppendLine($"경로: {credPath}");
        }
        else
        {
            sb.AppendLine("credentials 파일 존재");
            try
            {
                var fileJson = await File.ReadAllTextAsync(credPath, cancellationToken).ConfigureAwait(false);
                var file = JsonSerializer.Deserialize<ClaudeCredentialsFile>(fileJson, JsonOptions);
                var creds = file?.ClaudeAiOauth;

                if (creds is null)
                {
                    sb.AppendLine("claudeAiOauth 필드 없음");
                }
                else if (string.IsNullOrWhiteSpace(creds.AccessToken))
                {
                    sb.AppendLine("accessToken 비어있음");
                }
                else if (creds.IsExpired)
                {
                    sb.AppendLine($"토큰 만료됨: {DateTimeOffset.FromUnixTimeMilliseconds(creds.ExpiresAt):yyyy-MM-dd HH:mm} UTC");
                }
                else
                {
                    sb.AppendLine($"토큰 유효: plan={creds.SubscriptionType ?? "unknown"}, tier={creds.RateLimitTier ?? "unknown"}");
                    sb.AppendLine($"scopes: [{string.Join(", ", creds.Scopes)}]");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"credentials 읽기 실패: {ex.Message}");
            }
        }

        var claudePath = ResolveClaudePath(_claudeCliPath);
        sb.AppendLine(claudePath is null
            ? "Claude CLI 없음"
            : $"Claude CLI: {claudePath}");

        return sb.ToString().Trim();
    }

    public static string? FindClaudePath() => ResolveClaudePath(null);

    private async Task<ClaudeUsageSnapshot> GetUsageAutoAsync(CancellationToken cancellationToken)
    {
        Exception? oauthException = null;
        try
        {
            return await GetUsageViaOAuthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ClaudeTokenExpiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            oauthException = ex;
        }

        try
        {
            return await GetUsageViaCliAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception cliException)
        {
            var combined = oauthException is null
                ? cliException.Message
                : $"OAuth: {oauthException.Message}\nCLI: {cliException.Message}";
            throw new InvalidOperationException(combined, cliException);
        }
    }

    private async Task<ClaudeUsageSnapshot> GetUsageViaOAuthAsync(CancellationToken cancellationToken)
    {
        var (creds, isExpired) = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
        if (creds is null)
        {
            if (isExpired)
            {
                throw new ClaudeTokenExpiredException("Claude OAuth 토큰이 만료되었습니다. 'claude' 명령으로 다시 로그인하세요.");
            }

            throw new InvalidOperationException("Claude OAuth 토큰을 찾을 수 없습니다. 'claude' 명령으로 로그인하세요.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429 && _cachedSnapshot is not null)
            {
                return _cachedSnapshot;
            }

            var message = (int)response.StatusCode == 429
                ? "Claude usage API 요청 한도 초과(429). 잠시 후 다시 시도하세요."
                : $"Claude usage API HTTP {(int)response.StatusCode}: {Truncate(json)}";
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var snapshot = ParseOAuthUsageResponse(json);
        return snapshot with { Plan = creds.SubscriptionType ?? creds.RateLimitTier ?? snapshot.Plan };
    }

    private static async Task<(ClaudeOAuthCredentials? Creds, bool IsExpired)> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        var credPath = GetCredentialsPath();
        if (!File.Exists(credPath))
        {
            return (null, false);
        }

        try
        {
            var json = await File.ReadAllTextAsync(credPath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<ClaudeCredentialsFile>(json, JsonOptions);
            var creds = file?.ClaudeAiOauth;

            if (creds is null || string.IsNullOrWhiteSpace(creds.AccessToken))
            {
                return (null, false);
            }

            return creds.IsExpired ? (null, true) : (creds, false);
        }
        catch
        {
            return (null, false);
        }
    }

    private async Task<ClaudeUsageSnapshot> GetUsageViaCliAsync(CancellationToken cancellationToken)
    {
        var claudePath = ResolveClaudePath(_claudeCliPath)
            ?? throw new InvalidOperationException("Claude CLI를 찾을 수 없습니다. PATH 또는 ~/.claude/local 경로를 확인하세요.");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = claudePath,
            Arguments = "usage --format json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Claude CLI 프로세스를 시작하지 못했습니다.");

        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout) && stdout.TrimStart().StartsWith('{'))
        {
            return ParseOAuthUsageResponse(stdout);
        }

        return ParseCliTextOutput(stdout + "\n" + stderr);
    }

    private static ClaudeUsageSnapshot ParseOAuthUsageResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Claude usage API 응답이 비어있습니다.");
        }

        var response = JsonSerializer.Deserialize<ClaudeUsageApiResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Claude usage API 응답 파싱 실패. Raw: {Truncate(json)}");

        var session = ToRateWindow(response.FiveHour, 300);
        var weekly = ToRateWindow(response.SevenDay, 10080);
        var modelWindows = new Dictionary<string, ClaudeRateWindow>();

        if (response.SevenDayOpus is not null && ToRateWindow(response.SevenDayOpus, 10080) is { } opus)
        {
            modelWindows["Claude Opus (7d)"] = opus;
        }

        if (response.SevenDaySonnet is not null && ToRateWindow(response.SevenDaySonnet, 10080) is { } sonnet)
        {
            modelWindows["Claude Sonnet (7d)"] = sonnet;
        }

        if (session is null && weekly is null && modelWindows.Count == 0)
        {
            throw new InvalidOperationException($"Claude usage API 응답에 사용량 데이터가 없습니다. Raw: {Truncate(json)}");
        }

        return new ClaudeUsageSnapshot(session, weekly, modelWindows, response.RateLimitTier, null, DateTime.UtcNow);
    }

    private static ClaudeUsageSnapshot ParseCliTextOutput(string text)
    {
        text = Regex.Replace(text, @"\x1B\[[0-9;]*[mK]", "");

        ClaudeRateWindow? session = null;
        ClaudeRateWindow? weekly = null;
        string? plan = null;

        var pctMatches = Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*%");
        var timeMatch = Regex.Match(text, @"(\d+)\s*h(?:ours?)?\s*(\d+)\s*m(?:in)?", RegexOptions.IgnoreCase);

        DateTime? resetsAt = null;
        if (timeMatch.Success)
        {
            var hours = int.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            resetsAt = DateTime.UtcNow.AddHours(hours).AddMinutes(minutes);
        }

        foreach (var candidate in new[] { "max", "pro", "team", "free" })
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                plan = candidate;
                break;
            }
        }

        if (pctMatches.Count > 0 && double.TryParse(pctMatches[0].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct1))
        {
            session = new ClaudeRateWindow(pct1, 300, resetsAt, BuildResetDescription(resetsAt));
        }

        if (pctMatches.Count > 1 && double.TryParse(pctMatches[1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct2))
        {
            weekly = new ClaudeRateWindow(pct2, 10080, null, "");
        }

        return new ClaudeUsageSnapshot(session, weekly, new Dictionary<string, ClaudeRateWindow>(), plan, null, DateTime.UtcNow);
    }

    private static ClaudeRateWindow? ToRateWindow(ClaudeUsageWindowPayload? window, int windowMinutes)
    {
        if (window is null)
        {
            return null;
        }

        var resetsAt = window.ResetsAtUtc;
        return new ClaudeRateWindow(window.Utilization, windowMinutes, resetsAt, BuildResetDescription(resetsAt));
    }

    private static string BuildResetDescription(DateTime? resetsAt)
    {
        if (resetsAt is null)
        {
            return "";
        }

        var remaining = resetsAt.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "Resetting...";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h {remaining.Minutes}m";
        }

        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
            : $"{remaining.Minutes}m";
    }

    private static string? ResolveClaudePath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = "claude",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                if (process.ExitCode == 0)
                {
                    var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(firstLine) && File.Exists(firstLine))
                    {
                        return firstLine;
                    }
                }
            }
        }
        catch
        {
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wellKnown = new[]
        {
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
            @"C:\Program Files\nodejs\claude.cmd",
            @"C:\Program Files\nodejs\claude.exe",
            Path.Combine(appData, "npm", "claude.cmd"),
            Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
            Path.Combine(userProfile, ".claude", "local", "claude.exe"),
            Path.Combine(localAppData, "AnthropicClaude", "claude.exe"),
            Path.Combine(userProfile, ".local", "bin", "claude")
        };

        return wellKnown.FirstOrDefault(File.Exists);
    }

    private static string GetCredentialsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json");
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] + "..." : value;

    private void LoadCacheFromDisk()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return;
            }

            var json = File.ReadAllText(CacheFilePath);
            var snapshot = JsonSerializer.Deserialize<ClaudeUsageSnapshot>(json, JsonOptions);
            if (snapshot is not null)
            {
                _cachedSnapshot = snapshot;
                _cacheTimestamp = File.GetLastWriteTimeUtc(CacheFilePath);
            }
        }
        catch
        {
        }
    }

    private static void SaveCacheToDisk(ClaudeUsageSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch
        {
        }
    }

    private sealed class ClaudeCredentialsFile
    {
        [JsonPropertyName("claudeAiOauth")]
        public ClaudeOAuthCredentials? ClaudeAiOauth { get; set; }
    }

    private sealed class ClaudeOAuthCredentials
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresAt")]
        public long ExpiresAt { get; set; }

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; } = [];

        [JsonPropertyName("subscriptionType")]
        public string? SubscriptionType { get; set; }

        [JsonPropertyName("rateLimitTier")]
        public string? RateLimitTier { get; set; }

        public bool IsExpired => ExpiresAt > 0
            && DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAt).UtcDateTime < DateTime.UtcNow.AddMinutes(5);
    }

    private sealed class ClaudeUsageApiResponse
    {
        [JsonPropertyName("five_hour")]
        public ClaudeUsageWindowPayload? FiveHour { get; set; }

        [JsonPropertyName("seven_day")]
        public ClaudeUsageWindowPayload? SevenDay { get; set; }

        [JsonPropertyName("seven_day_opus")]
        public ClaudeUsageWindowPayload? SevenDayOpus { get; set; }

        [JsonPropertyName("seven_day_sonnet")]
        public ClaudeUsageWindowPayload? SevenDaySonnet { get; set; }

        [JsonPropertyName("extra_usage")]
        public ClaudeExtraUsagePayload? ExtraUsage { get; set; }

        [JsonPropertyName("rate_limit_tier")]
        public string? RateLimitTier { get; set; }
    }

    private sealed class ClaudeUsageWindowPayload
    {
        [JsonPropertyName("utilization")]
        public double Utilization { get; set; }

        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }

        public DateTime? ResetsAtUtc => DateTime.TryParse(ResetsAt, out var dateTime)
            ? dateTime.ToUniversalTime()
            : null;
    }

    private sealed class ClaudeExtraUsagePayload
    {
        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("used_credits")]
        public double? UsedCredits { get; set; }

        [JsonPropertyName("monthly_limit")]
        public double? MonthlyLimit { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }
}

public sealed record ClaudeUsageSnapshot(
    ClaudeRateWindow? Session,
    ClaudeRateWindow? Weekly,
    IReadOnlyDictionary<string, ClaudeRateWindow> ModelWindows,
    string? Plan,
    string? Email,
    DateTime UpdatedAt);

public sealed record ClaudeRateWindow(
    double Utilization,
    int WindowMinutes,
    DateTime? ResetsAtUtc,
    string ResetDescription);
