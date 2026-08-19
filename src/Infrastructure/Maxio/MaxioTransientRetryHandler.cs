using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio 429 (concurrency throttle) and transient 5xx responses with backoff.
/// Billing API limits concurrent in-flight calls per subdomain; callers should not
/// increase parallelism when throttled.
/// </summary>
internal sealed class MaxioTransientRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            var attemptRequest = attempt == 1 ? request : await CloneAsync(request);
            response = await base.SendAsync(attemptRequest, cancellationToken);

            if (!ShouldRetry(response.StatusCode) || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
            if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > delay)
            {
                delay = retryAfter;
            }

            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static bool ShouldRetry(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests
        || statusCode == System.Net.HttpStatusCode.BadGateway
        || statusCode == System.Net.HttpStatusCode.ServiceUnavailable
        || statusCode == System.Net.HttpStatusCode.GatewayTimeout;

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
