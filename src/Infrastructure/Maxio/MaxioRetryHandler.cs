using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures — throttling (429), server errors (5xx), connection faults and
/// per-attempt timeouts — with exponential backoff plus jitter, honouring a Retry-After header when
/// Maxio sends one.
/// </summary>
/// <remarks>
/// Only idempotent request methods are retried automatically. A POST is retried solely on 429 and
/// 503, where Maxio has stated it did not process the request; retrying a POST after an ambiguous
/// 500 or a socket error could create a second customer or subscription.
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
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryBaseDelayMilliseconds));

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (attempt >= maxAttempts || !ShouldRetry(request.Method, response.StatusCode))
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransportFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempts || !IsAutomaticallyRetryable(request.Method))
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = response is not null
                ? RetryAfter(response) ?? Backoff(baseDelay, attempt)
                : Backoff(baseDelay, attempt);

            _logger.LogWarning(
                transportFailure,
                "Maxio request {Method} {Path} failed with {Outcome} (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                attempt,
                maxAttempts,
                (int)delay.TotalMilliseconds);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpMethod method, HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Maxio rejected the request without acting on it, so replaying it is safe for any method.
            return true;
        }

        return statusCode >= HttpStatusCode.InternalServerError && IsAutomaticallyRetryable(method);
    }

    /// <summary>
    /// True for methods the HTTP specification defines as idempotent, where replaying a request
    /// whose outcome is unknown cannot duplicate a side effect.
    /// </summary>
    private static bool IsAutomaticallyRetryable(HttpMethod method) =>
        method == HttpMethod.Get ||
        method == HttpMethod.Head ||
        method == HttpMethod.Options ||
        method == HttpMethod.Put ||
        method == HttpMethod.Delete;

    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException ||
        exception is TaskCanceledException or OperationCanceledException;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Cap(delta);
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Cap(wait);
            }
        }

        return null;
    }

    private static TimeSpan Backoff(TimeSpan baseDelay, int attempt)
    {
        var exponential = baseDelay * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.25 + 0.875; // +/- 12.5%, so retries do not align.
        return Cap(exponential * jitter);
    }

    private static TimeSpan Cap(TimeSpan delay) => delay > TimeSpan.FromSeconds(20) ? TimeSpan.FromSeconds(20) : delay;
}
