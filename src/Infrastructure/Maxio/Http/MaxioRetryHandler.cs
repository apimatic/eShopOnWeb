using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries Maxio calls that failed in a way that is safe to repeat.
/// </summary>
/// <remarks>
/// The policy is deliberately conservative about writes. A read can always be repeated, and a
/// rate-limited request was never processed, so both are retried. A write that failed with a server
/// error or a dropped connection may well have been applied on the provider side, so it is never
/// retried here: repeating it could enroll a shopper twice. Recovery for those cases lives in
/// <c>MaxioSubscriptionService</c>, which re-resolves the intended subscription by its reference
/// instead of blindly resending.
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;
        var isRepeatableMethod = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        if (maxAttempts > 1 && request.Content is not null)
        {
            // Buffer the body once so a retried request can send exactly the same bytes.
            await request.Content.LoadIntoBufferAsync();
        }

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (!isLastAttempt && isRepeatableMethod)
            {
                _logger.LogWarning(
                    ex,
                    "Maxio {Method} attempt {Attempt} of {MaxAttempts} failed to connect; retrying.",
                    request.Method, attempt, maxAttempts);

                await DelayAsync(options, attempt, retryAfter: null, cancellationToken);
                continue;
            }

            if (isLastAttempt || !ShouldRetry(response.StatusCode, isRepeatableMethod))
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                             ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

            _logger.LogWarning(
                "Maxio {Method} attempt {Attempt} of {MaxAttempts} returned {StatusCode}; retrying.",
                request.Method, attempt, maxAttempts, (int)response.StatusCode);

            response.Dispose();
            await DelayAsync(options, attempt, retryAfter, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRepeatableMethod)
    {
        // A rate-limited request was rejected before it was processed, so repeating it is safe
        // whatever the verb.
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (!isRepeatableMethod)
        {
            return false;
        }

        return statusCode == HttpStatusCode.RequestTimeout
               || (int)statusCode >= 500;
    }

    private static Task DelayAsync(
        MaxioOptions options,
        int attempt,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            // Never wait longer than the call budget just because the provider asked us to.
            var capped = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds, options.TimeoutSeconds));
            return Task.Delay(capped, cancellationToken);
        }

        var baseDelay = Math.Max(1, options.RetryBaseDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt - 1);

        // Jitter keeps a burst of parallel callers from retrying in lockstep.
        var jitter = Random.Shared.NextDouble() * baseDelay;

        return Task.Delay(TimeSpan.FromMilliseconds(exponential + jitter), cancellationToken);
    }
}
