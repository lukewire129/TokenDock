using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class OpenAiUsageErrorFormatterTests
{
    [Fact]
    public void Format_ReturnsLoginExpiredMessageForUnauthorized()
    {
        var message = OpenAiUsageErrorFormatter.Format(new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));

        Assert.Equal("로그인이 만료된 것 같아요. 계정을 다시 연결하세요.", message);
    }

    [Fact]
    public void Format_ReturnsNetworkMessageForRequestWithoutStatusCode()
    {
        var message = OpenAiUsageErrorFormatter.Format(new HttpRequestException("network"));

        Assert.Equal("네트워크 연결을 확인하세요.", message);
    }

    [Fact]
    public void Format_ReturnsResponseChangedMessageForJsonException()
    {
        var message = OpenAiUsageErrorFormatter.Format(new JsonException("bad json"));

        Assert.Equal("OpenAI 응답 형식이 바뀐 것 같아요. 업데이트가 필요할 수 있습니다.", message);
    }

    [Fact]
    public void Format_ReturnsNetworkMessageForTimeout()
    {
        var message = OpenAiUsageErrorFormatter.Format(new TaskCanceledException("timeout"));

        Assert.Equal("네트워크 연결을 확인하세요.", message);
    }
}
