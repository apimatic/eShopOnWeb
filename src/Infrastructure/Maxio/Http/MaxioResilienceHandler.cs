using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries transient Maxio failures with exponential back-off plus jitter, honouring
/// <c>Retry-After</c> when the provider sends it.
/// </summary>
/// <remarks>
/// Requests that are not safe to repeat (anything other than GET/HEAD) are retried only on
/// <c>429 Too Many Requests</c>, where the provider has definitively not processed the request.
/// Retrying a POST after a 5xx or a dropped connection could enrol a shopper twice, so it is not
/// done here; the enrolment flow guards its own repeat-safety instead.
/// </remarks>
public class MaxioResilienceHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioResilienceHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;
        var safeToRepeat = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (attempt >= maxAttempts || !ShouldRetry(response.StatusCode, safeToRepeat))
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransientTransportFailure(ex, cancellationToken))
            {
                if (attempt >= maxAttempts || !safeToRepeat)
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = GetRetryDelay(response, attempt, options.RetryBaseDelay);

            _logger.LogWarning(
                "Maxio {Method} {Path} attempt {Attempt}/{MaxAttempts} failed ({Reason}); retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool safeToRepeat)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return safeToRepeat && (int)statusCode >= 500;
    }

    private static bool IsTransientTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt, TimeSpan baseDelay)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            var provided = retryAfter.Delta
                ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);

            if (provided is { } wait && wait > TimeSpan.Zero)
            {
                return wait < MaxBackoff ? wait : MaxBackoff;
            }
        }

        // Exponential back-off with full jitter, so concurrent callers do not retry in lockstep.
        var exponential = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var capped = exponential < MaxBackoff ? exponential : MaxBackoff;

        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped.TotalMilliseconds);
    }
}
