using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio calls that are safe to repeat, with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// <para>
/// Read operations are retried on transport faults, timeouts and 5xx responses. Writes are not: a
/// <c>POST /subscriptions.json</c> that times out may well have succeeded, and re-sending it could
/// enroll the shopper twice.
/// </para>
/// <para>
/// <c>429 Too Many Requests</c> is retried for every method, because a throttled request was by
/// definition not processed. When Maxio sends <c>Retry-After</c> it is honoured verbatim.
/// </para>
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

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
        var isIdempotent = IsIdempotent(request.Method);

        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();

            Exception? transportFailure = null;
            response = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                transportFailure = ex;
            }

            var lastAttempt = attempt >= maxAttempts;
            if (transportFailure is not null)
            {
                if (lastAttempt || !isIdempotent)
                {
                    throw transportFailure;
                }
            }
            else if (!ShouldRetry(response!, isIdempotent) || lastAttempt)
            {
                return response!;
            }

            var delay = GetDelay(response, attempt, options);
            _logger.LogWarning(
                "Maxio request {Method} {Path} attempt {Attempt}/{MaxAttempts} failed ({Outcome}); retrying in {Delay}.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                transportFailure?.GetType().Name ?? ((int)response!.StatusCode).ToString(),
                delay);

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static bool ShouldRetry(HttpResponseMessage response, bool isIdempotent)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return isIdempotent && (int)response.StatusCode >= 500;
    }

    private static TimeSpan GetDelay(HttpResponseMessage? response, int attempt, MaxioOptions options)
    {
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

        var baseDelayMs = Math.Max(1, options.RetryBaseDelayMilliseconds);
        var backoffMs = baseDelayMs * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(0, baseDelayMs);
        var delay = TimeSpan.FromMilliseconds(backoffMs + jitterMs);

        return delay > MaxBackoff ? MaxBackoff : delay;
    }
}
