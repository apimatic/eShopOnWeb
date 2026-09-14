using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Concurrency limiting plus bounded retry with exponential backoff and jitter for Maxio calls.
/// Maxio throttles by concurrency rather than request rate and asks callers to back off - not
/// parallelise - when they see 429, so retries are serialised behind <see cref="MaxioRequestGate"/>.
/// </summary>
public sealed class MaxioResilienceHandler : DelegatingHandler
{
    /// <summary>
    /// Set by <see cref="MaxioApiClient"/> on requests that carry a duplicate-prevention token,
    /// which makes them safe to replay even though they are not HTTP-idempotent.
    /// </summary>
    internal static readonly HttpRequestOptionsKey<bool> SafeToRetryKey = new("Maxio.SafeToRetry");

    private readonly MaxioRequestGate _gate;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(MaxioRequestGate gate, IOptions<MaxioOptions> options,
        ILogger<MaxioResilienceHandler> logger)
    {
        _gate = gate;
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var maxAttempts = Math.Max(0, settings.MaxRetryAttempts) + 1;
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, settings.RetryBaseDelayMilliseconds));
        var replayable = IsReplayable(request);

        for (var attempt = 1; ; attempt++)
        {
            var lastAttempt = attempt >= maxAttempts;
            HttpResponseMessage? response = null;

            try
            {
                using (await _gate.EnterAsync(cancellationToken))
                {
                    response = await base.SendAsync(request, cancellationToken);
                }

                if (lastAttempt || !ShouldRetry(response.StatusCode, replayable))
                {
                    return response;
                }

                var delay = ResolveDelay(response, attempt, baseDelay);
                _logger.LogWarning(
                    "Maxio responded {StatusCode} to {Method} {Path}; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}).",
                    (int)response.StatusCode, request.Method, request.RequestUri?.AbsolutePath, delay.TotalMilliseconds,
                    attempt, maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && !lastAttempt && replayable)
            {
                response?.Dispose();

                var delay = Backoff(attempt, baseDelay);
                _logger.LogWarning(ex,
                    "Maxio call {Method} {Path} failed transiently; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}).",
                    request.Method, request.RequestUri?.AbsolutePath, delay.TotalMilliseconds, attempt, maxAttempts);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// GET/HEAD are always replayable; anything else only when the caller marked it safe by
    /// including a duplicate-prevention token.
    /// </summary>
    private static bool IsReplayable(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
        {
            return true;
        }

        return request.Options.TryGetValue(SafeToRetryKey, out var safe) && safe;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool replayable)
    {
        // A 429 means Maxio refused to run the request, so replaying it is always safe.
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return replayable && (int)statusCode >= 500;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        // A cancelled TaskCanceledException with an untriggered token is HttpClient's timeout.
        OperationCanceledException when !cancellationToken.IsCancellationRequested => true,
        HttpRequestException => true,
        _ => false
    };

    private static TimeSpan ResolveDelay(HttpResponseMessage response, int attempt, TimeSpan baseDelay)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Cap(delta);
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return Cap(until);
            }
        }

        return Backoff(attempt, baseDelay);
    }

    private static TimeSpan Backoff(int attempt, TimeSpan baseDelay)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        // Full jitter: spreads concurrent retries instead of re-colliding on the same tick.
        var jittered = Random.Shared.NextDouble() * exponential;
        return Cap(TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds + jittered));
    }

    private static TimeSpan Cap(TimeSpan delay) =>
        delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay;
}
