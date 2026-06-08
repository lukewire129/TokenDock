using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TokenDock.Services;

public static class OpenAiUsageErrorFormatter
{
    public static string Format(Exception exception)
    {
        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                "로그인이 만료된 것 같아요. 계정을 다시 연결하세요.",
            HttpRequestException { StatusCode: null } =>
                "네트워크 연결을 확인하세요.",
            HttpRequestException =>
                "OpenAI 사용량을 가져오지 못했습니다. 잠시 후 다시 시도하세요.",
            JsonException =>
                "OpenAI 응답 형식이 바뀐 것 같아요. 업데이트가 필요할 수 있습니다.",
            TaskCanceledException =>
                "네트워크 연결을 확인하세요.",
            _ =>
                $"사용량 확인 실패: {exception.Message}"
        };
    }
}
