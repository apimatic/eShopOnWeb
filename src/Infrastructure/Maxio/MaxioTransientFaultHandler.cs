using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring Retry-After.
///
/// Safe (read-only) requests are retried on network faults, timeouts, HTTP 429 and 5xx.
/// State-changing requests (POST) are retried only on HTTP 429, where the API has explicitly
/// rejected the call without processing it - anything else could have been applied server side and
/// must not be replayed blindly. Higher level idempotency for subscribe lives in
/// <see cref="MaxioSubscriptionService"/>.
/// </summary>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioTransientFaultHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = settings.ResolveRetryAttempts() + 1;
        var baseDelay = settings.ResolveRetryBaseDelay();
        var isSafeRequest = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (isLastAttempt || !ShouldRetry(response.StatusCode, isSafeRequest))
                {
                    return response;
                }

                var delay = GetRetryAfter(response) ?? ComputeBackoff(baseDelay, attempt);
                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode}; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})",
                    request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode, delay.TotalMilliseconds, attempt, maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientException(ex, cancellationToken) && isSafeRequest && !isLastAttempt)
            {
                var delay = ComputeBackoff(baseDelay, attempt);
                _logger.LogWarning(ex,
                    "Maxio {Method} {Path} failed with a transient error; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts})",
                    request.Method, request.RequestUri?.PathAndQuery, delay.TotalMilliseconds, attempt, maxAttempts);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isSafeRequest)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return isSafeRequest && (int)statusCode >= 500;
    }

    private static bool IsTransientException(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException => true,
        TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    private static TimeSpan ComputeBackoff(TimeSpan baseDelay, int attempt)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, TimeSpan.FromSeconds(30).TotalMilliseconds));
    }
}
