using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and full jitter.
/// <para>
/// Maxio limits by concurrency rather than request rate and asks callers to back off — not to
/// parallelise — when throttled, so retries are serial and honour <c>Retry-After</c> when present.
/// Only requests that are safe to repeat are retried: reads, and writes explicitly marked safe by
/// the client because they carry a uniqueness token.
/// </para>
/// </summary>
public class MaxioResilienceHandler : DelegatingHandler
{
    /// <summary>Set by the client on writes that carry a uniqueness token and may therefore be replayed.</summary>
    public static readonly HttpRequestOptionsKey<bool> RetrySafeOption = new("Maxio.RetrySafe");

    private readonly ILogger<MaxioResilienceHandler> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _attemptTimeout;

    public MaxioResilienceHandler(ILogger<MaxioResilienceHandler> logger, int maxRetries, TimeSpan baseDelay, TimeSpan attemptTimeout)
    {
        _logger = logger;
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = baseDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(500) : baseDelay;
        _attemptTimeout = attemptTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : attemptTimeout;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var canRetry = IsRetrySafe(request);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            // Each attempt gets its own budget; a slow first attempt must not eat the retries.
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(_attemptTimeout);

            try
            {
                response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The attempt timeout elapsed rather than the caller giving up.
                transportFailure = new TimeoutException(
                    $"The Maxio request timed out after {_attemptTimeout.TotalSeconds:0.#}s.", ex);
            }

            if (!canRetry || attempt >= _maxRetries)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            var delay = ComputeDelay(attempt, response);
            _logger.LogWarning(
                "Maxio {Method} {Path} failed ({Outcome}); retrying in {Delay}ms (attempt {Attempt} of {MaxRetries}).",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                delay.TotalMilliseconds,
                attempt + 1,
                _maxRetries);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetrySafe(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get ||
        request.Method == HttpMethod.Head ||
        (request.Options.TryGetValue(RetrySafeOption, out var retrySafe) && retrySafe);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        // A Retry-After from the server always beats our own guess.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var ceiling = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling + _baseDelay.TotalMilliseconds);
    }
}
