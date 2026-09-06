using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio calls that failed for reasons a retry can fix: rate limiting, upstream 5xx, and
/// connection-level faults.
/// </summary>
/// <remarks>
/// <para>
/// Retrying a POST is normally unsafe, but it is safe here by construction: every write this
/// integration issues carries a deterministic <c>reference</c> whose uniqueness Maxio enforces
/// site-wide. If a retried request duplicates one that actually reached Maxio, Maxio rejects the
/// duplicate with 422 "Reference: must be unique", and
/// <see cref="MaxioSubscriptionBillingService"/> resolves that into the record that already exists.
/// A retry can therefore never produce a second customer or a second subscription.
/// </para>
/// <para>
/// Backoff is exponential with full jitter so a fleet recovering from a rate-limit window does not
/// resynchronise into another one. A <c>Retry-After</c> header, when Maxio sends one, wins.
/// </para>
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(
        IOptionsMonitor<MaxioSettings> settings,
        ILogger<MaxioTransientFaultHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_settings.CurrentValue.MaxAttempts, 1, 5);

        if (maxAttempts == 1)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // A request body can only be written to the wire once, so capture it up front and rebuild a
        // fresh message per attempt rather than re-sending a message that has already been consumed.
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 1; ; attempt++)
        {
            var attemptRequest = attempt == 1 ? request : Clone(request, body);
            HttpResponseMessage? response = null;
            Exception? transientFailure = null;

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

                if (!IsTransient(response.StatusCode) || attempt >= maxAttempts)
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < maxAttempts)
            {
                transientFailure = ex;
            }
            finally
            {
                if (!ReferenceEquals(attemptRequest, request))
                {
                    attemptRequest.Dispose();
                }
            }

            var delay = response is not null ? DelayFor(response, attempt) : DelayFor(attempt);

            _logger.LogWarning(
                transientFailure,
                "Transient Maxio failure on {Method} {Path} (attempt {Attempt}/{MaxAttempts}, outcome {Outcome}); retrying in {DelayMs} ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                response is not null ? ((int)response.StatusCode).ToString() : transientFailure?.GetType().Name,
                delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };

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

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException => true,
        // A cancellation raised while the caller's token is signalled is a real cancellation, not a
        // timeout, and must propagate untouched.
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };

    private static TimeSpan DelayFor(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxDelay);
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Min(wait, MaxDelay);
            }
        }

        return DelayFor(attempt);
    }

    private static TimeSpan DelayFor(int attempt)
    {
        var exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);

        // Full jitter: sample uniformly from [0, capped] rather than always waiting the whole window.
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
