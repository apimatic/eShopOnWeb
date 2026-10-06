using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retry handler for read-only (GET) Billing API calls that fail transiently
/// (connection problems, timeouts, or 429/5xx responses). The Billing API
/// throttles with 429 and documents a slot-based concurrency limit.
/// Mutating calls are deliberately not retried here: enrollment requests are
/// deduplicated upstream via deterministic references and uniqueness tokens,
/// which makes blind transport retries of POSTs unnecessary.
/// </summary>
public sealed class MaxioTransientRetryHandler : DelegatingHandler
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!HttpMethod.Get.Equals(request.Method))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        HttpResponseMessage? lastResponse = null;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                lastResponse = await base.SendAsync(Clone(request), cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaximumAttempts)
            {
                continue;
            }
            catch (TaskCanceledException) when (attempt < MaximumAttempts && !cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (lastResponse!.IsSuccessStatusCode ||
                !IsTransient(lastResponse.StatusCode) ||
                attempt == MaximumAttempts)
            {
                return lastResponse;
            }

            var retryAfter = GetRetryAfter(lastResponse);
            lastResponse.Dispose();
            lastResponse = null;
            await Task.Delay(retryAfter ?? (BaseDelay * Math.Pow(2, attempt - 1)), cancellationToken);
        }

        // Unreachable: the loop always returns or rethrows.
        throw new InvalidOperationException("Maxio retry handler exited unexpectedly.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is null)
        {
            return null;
        }

        return response.Headers.RetryAfter.Delta
            ?? (response.Headers.RetryAfter.Date switch
            {
                { } when response.Headers.RetryAfter.Date > DateTimeOffset.UtcNow =>
                    response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow,
                _ => null,
            });
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
