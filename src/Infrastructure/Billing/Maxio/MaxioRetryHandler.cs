using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries throttled, transient and network-failed calls with exponential backoff plus jitter.
/// Maxio limits concurrency rather than request rate, so backing off (instead of retrying tightly
/// or fanning out) is the documented way to recover from a 429.
/// </summary>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioRetryHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transientFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex)
            {
                transientFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation that the caller did not ask for.
                transientFailure = ex;
            }

            if (attempt >= maxAttempts)
            {
                if (transientFailure is not null) throw transientFailure;
                return response!;
            }

            var delay = ComputeDelay(attempt, options.RetryBaseDelayMilliseconds, response);
            _logger.LogWarning(transientFailure,
                "Maxio call {Method} {Path} failed ({Outcome}); retrying in {Delay}ms (attempt {Attempt}/{MaxAttempts}).",
                request.Method, request.RequestUri?.AbsolutePath,
                transientFailure?.GetType().Name ?? response!.StatusCode.ToString(),
                delay.TotalMilliseconds, attempt, maxAttempts);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static TimeSpan ComputeDelay(int attempt, int baseDelayMs, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }

        var backoffMs = baseDelayMs * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(0, Math.Max(1, baseDelayMs));
        return TimeSpan.FromMilliseconds(Math.Min(backoffMs + jitterMs, 10_000));
    }
}
