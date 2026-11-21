using System.Net;
using System.Net.Http;

namespace ForceBond.Launcher;

internal static class AccessGetRetry
{
    // Only safe GET requests are retried; sign-in/logout POST requests are not replayed.
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client,
        Func<HttpRequestMessage> createRequest, CancellationToken token,
        Action<int, HttpStatusCode>? report = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= (duration, cancellation) => Task.Delay(duration, cancellation);
        for (int attempt = 1; ; attempt++)
        {
            using var request = createRequest();
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Only GET requests can be retried.");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            bool temporary = response.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!temporary || attempt >= 3) return response;
            var retry = response.Headers.RetryAfter;
            TimeSpan pause = retry?.Delta ?? (retry?.Date is { } date
                ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt));
            pause = TimeSpan.FromSeconds(Math.Clamp(pause.TotalSeconds, 0, 2));
            var status = response.StatusCode;
            response.Dispose();
            report?.Invoke(attempt, status);
            await delay(pause, token);
        }
    }
}
