using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// Retries transient Maxio failures with jittered exponential backoff, honouring
/// <c>Retry-After</c> when the API sends it.
/// <para>
/// Retry safety matters more than retry coverage here: replaying <c>POST /customers.json</c> or
/// <c>POST /subscriptions.json</c> after an ambiguous 5xx could enroll a shopper twice, which is
/// exactly what the subscribe flow must never do. So unsafe methods are retried only on 429,
/// where the specification's rate-limit semantics guarantee the request was rejected before it was
/// processed. Safe methods (GET/HEAD) are retried on the full transient set.
/// </para>
/// </summary>
public sealed class MaxioResilienceHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioResilienceHandler> logger)
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
        var isSafeMethod = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!ShouldRetry(response.StatusCode, isSafeMethod) || attempt >= maxAttempts)
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (isSafeMethod && attempt < maxAttempts)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (isSafeMethod && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                // A TaskCanceledException without a cancelled token is the HttpClient timeout.
                transportFailure = ex;
            }

            var delay = ComputeDelay(options, attempt, response);

            if (transportFailure is not null)
            {
                _logger.LogWarning(
                    transportFailure,
                    "Maxio request {Method} {Path} failed to complete (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs} ms.",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Maxio request {Method} {Path} returned {StatusCode} (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs} ms.",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    (int)response!.StatusCode,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);

                response.Dispose();
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isSafeMethod)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (!isSafeMethod)
        {
            return false;
        }

        return statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;
    }

    private static TimeSpan ComputeDelay(MaxioOptions options, int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var untilDate = date - DateTimeOffset.UtcNow;
                if (untilDate > TimeSpan.Zero)
                {
                    return untilDate;
                }
            }
        }

        var baseDelayMs = Math.Max(1, options.RetryBaseDelayMilliseconds);
        var exponentialMs = baseDelayMs * Math.Pow(2, attempt - 1);

        // Full jitter: spreads retries from concurrent callers instead of synchronising them.
        var jitteredMs = Random.Shared.NextDouble() * exponentialMs;

        return TimeSpan.FromMilliseconds(Math.Min(jitteredMs, 30_000));
    }
}
