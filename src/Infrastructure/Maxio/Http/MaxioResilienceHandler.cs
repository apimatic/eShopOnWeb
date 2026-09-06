using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and full jitter.
/// <para>
/// Retries are deliberately asymmetric: throttling (429) is always safe to replay because the request was
/// rejected before it was processed, but a 5xx or a dropped connection on a non-idempotent request (notably
/// <c>POST /subscriptions.json</c>) may have been applied server side. Replaying those could enrol a shopper
/// twice, so only idempotent methods are retried for those failures. Recovery for a lost <c>POST</c> response
/// is handled one level up, by re-checking the shopper's existing subscriptions before creating a new one.
/// </para>
/// </summary>
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
        var idempotent = IsIdempotent(request.Method);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancelled task.
                transportFailure = ex;
            }

            var throttled = response is not null && response.StatusCode == HttpStatusCode.TooManyRequests;
            var serverFault = response is not null && (int)response.StatusCode >= 500;
            var retryable = throttled || ((serverFault || transportFailure is not null) && idempotent);

            if (!retryable || attempt >= maxAttempts)
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var delay = ComputeDelay(options, attempt, response);

            _logger.LogWarning(
                "Maxio request {Method} {Path} failed with {Outcome} (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs} ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) : transportFailure!.GetType().Name,
                attempt,
                maxAttempts,
                (int)delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put ||
        method == HttpMethod.Delete || method == HttpMethod.Options || method == HttpMethod.Trace;

    private static TimeSpan ComputeDelay(MaxioOptions options, int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return Min(delta, MaxBackoff);
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return Min(until, MaxBackoff);
                }
            }
        }

        var exponential = TimeSpan.FromMilliseconds(options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var capped = Min(exponential, MaxBackoff);

        // Full jitter: spreads retries out so that concurrent callers do not resynchronise on the provider.
        return TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Max(1, capped.TotalMilliseconds)));
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
