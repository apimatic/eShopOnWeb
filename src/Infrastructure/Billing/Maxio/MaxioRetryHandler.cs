using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Advanced Billing failures with exponential backoff and full jitter.
///
/// The retry policy is deliberately asymmetric. A GET can always be replayed. A POST cannot,
/// because a 5xx or a dropped connection leaves it genuinely unknown whether the billing system
/// created the record - replaying it risks a duplicate customer or a duplicate subscription. The
/// one exception is 429, where the billing system is explicitly telling us it did not process the
/// request, so replay is safe for any method.
///
/// Retry-safety for POST is instead provided a layer up, by caller-assigned unique references:
/// see <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = Math.Max(0, settings.MaxRetryAttempts) + 1;
        var replayable = IsReplayable(request.Method);

        // A sent HttpRequestMessage cannot be reused, so buffer the body once and rebuild the
        // message per attempt. Bodies here are small JSON documents.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;
            using var attemptRequest = Clone(request, body);

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

                if (!ShouldRetry(response.StatusCode, replayable))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (replayable)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (replayable && !cancellationToken.IsCancellationRequested)
            {
                // Only a cancellation that did not come from our own token is a failure worth
                // retrying. HttpClient folds its Timeout into the token it passes down, so an
                // expired time budget - like a caller aborting - propagates instead of looping.
                transportFailure = ex;
            }

            if (attempt >= maxAttempts)
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var delay = ComputeDelay(settings, attempt, response);
            _logger.LogWarning(
                "Maxio call {Method} {Path} failed transiently (attempt {Attempt}/{MaxAttempts}, {Outcome}); retrying in {DelayMs} ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                response is null ? transportFailure?.GetType().Name : ((int)response.StatusCode).ToString(),
                (int)delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }

    private static bool IsReplayable(System.Net.Http.HttpMethod method) =>
        method == System.Net.Http.HttpMethod.Get
        || method == System.Net.Http.HttpMethod.Head
        || method == System.Net.Http.HttpMethod.Options;

    private static bool ShouldRetry(HttpStatusCode status, bool replayable)
    {
        // 429 means the request was rejected before it was processed, so replay is always safe.
        if (status == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return replayable && (int)status >= 500;
    }

    private static TimeSpan ComputeDelay(MaxioSettings settings, int attempt, HttpResponseMessage? response)
    {
        // Honour an explicit Retry-After over our own backoff - the billing system knows better.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return until;
                }
            }
        }

        var baseDelay = settings.RetryBaseDelay <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(250)
            : settings.RetryBaseDelay;

        // Exponential backoff with full jitter, so concurrent callers do not retry in lockstep.
        var ceiling = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }
}
