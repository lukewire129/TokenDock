using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class OpenAiUsageServiceTests
{
    [Fact]
    public async Task GetUsageAsync_MapsCodexUsagePayload()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://chatgpt.com/backend-api/wham/usage", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
            Assert.True(request.Headers.TryGetValues("ChatGPT-Account-Id", out var accountIds));
            Assert.Contains("account-id", accountIds);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "plan_type": "pro",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 42,
                      "limit_window_seconds": 18000,
                      "reset_at": 1710000000
                    },
                    "secondary_window": {
                      "used_percent": 84,
                      "limit_window_seconds": 604800,
                      "reset_at": 1710604800
                    }
                  },
                  "credits": {
                    "has_credits": true,
                    "unlimited": false,
                    "balance": "9.99"
                  },
                  "rate_limit_reached_type": {
                    "kind": "workspace_member_usage_limit_reached"
                  },
                  "additional_rate_limits": [
                    {
                      "limit_name": "Codex Fast",
                      "metered_feature": "codex_fast",
                      "rate_limit": {
                        "primary_window": {
                          "used_percent": 10,
                          "limit_window_seconds": 300,
                          "reset_at": 1710000300
                        }
                      }
                    }
                  ]
                }
                """)
            };
        }));
        var service = new OpenAiUsageService(httpClient);

        var usage = await service.GetUsageAsync("access-token", "account-id");

        Assert.Equal("pro", usage.PlanType);
        Assert.Equal(42, usage.FiveHourLimit?.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), usage.FiveHourLimit?.WindowDuration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1710000000), usage.FiveHourLimit?.ResetsAt);
        Assert.Equal(84, usage.WeeklyLimit?.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), usage.WeeklyLimit?.WindowDuration);
        Assert.Equal("workspace_member_usage_limit_reached", usage.RateLimitReachedType);
        Assert.True(usage.Credits?.HasCredits);
        Assert.False(usage.Credits?.Unlimited);
        Assert.Equal("9.99", usage.Credits?.Balance);

        var additionalLimit = Assert.Single(usage.AdditionalLimits);
        Assert.Equal("codex_fast", additionalLimit.LimitId);
        Assert.Equal("Codex Fast", additionalLimit.LimitName);
        Assert.Equal(10, additionalLimit.Primary?.UsedPercent);
        Assert.Null(additionalLimit.Secondary);
    }

    [Fact]
    public async Task GetUsageAsync_NormalizesChatGptBaseUrl()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal("https://chatgpt.com/backend-api/wham/usage", request.RequestUri?.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "plan_type": "plus",
                  "rate_limit": null,
                  "credits": null,
                  "additional_rate_limits": null,
                  "rate_limit_reached_type": null
                }
                """)
            };
        }));
        var service = new OpenAiUsageService(httpClient, "https://chatgpt.com");

        var usage = await service.GetUsageAsync("access-token");

        Assert.Equal("plus", usage.PlanType);
        Assert.Null(usage.FiveHourLimit);
        Assert.Null(usage.WeeklyLimit);
        Assert.Empty(usage.AdditionalLimits);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsForUnsuccessfulResponse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized")
            }));
        var service = new OpenAiUsageService(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.GetUsageAsync("access-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("unauthorized", exception.Message);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
