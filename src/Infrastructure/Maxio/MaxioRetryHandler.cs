using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries throttled and transient Maxio responses with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// <para>
/// Maxio limits a site to a handful of concurrent calls and answers <c>429 Too Many Requests</c> once
/// that budget is exceeded, so the correct reaction is to slow down rather than fan out. Backoff is
/// serial, honours <c>Retry-After</c> when present, and is bounded by
/// <see cref="MaxioSettings.MaxRetryAttempts"/>.
/// </para>
/// <para>
/// Replaying a POST is safe here because every write this integration issues is guarded: customer
/// creates by a reference Maxio enforces as unique, subscription creates by a <c>uniqueness_token</c>.
/// A duplicate delivery comes back as a rejection rather than a second write, and the calling service
/// resolves it by re-reading the shopper's current state.
/// </para>
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = Math.Max(0, settings.MaxRetryAttempts) + 1;
        var attemptTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            Exception? transportFailure = null;

            try
            {
                response?.Dispose();

                // The per-attempt timeout lives here rather than on HttpClient.Timeout, which would
                // otherwise cap the whole retry sequence including its backoff delays.
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(attemptTimeout);

                response = await base.SendAsync(request, attemptCts.Token);

                // Buffer inside the attempt scope: it brings the body under the per-attempt timeout,
                // and it means nothing is still reading from attemptCts.Token once it is disposed.
                await BufferAsync(response, attemptCts.Token);

                if (!ShouldRetry(response.StatusCode) || attempt >= maxAttempts)
                {
                    return response;
                }
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken) && attempt < maxAttempts)
            {
                transportFailure = exception;
                response = null;
            }

            var delay = DelayFor(attempt, response);

            _logger.LogWarning(
                transportFailure,
                "Maxio call {Method} {Path} failed on attempt {Attempt}/{MaxAttempts} ({Outcome}); retrying in {Delay}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt,
                maxAttempts,
                transportFailure?.GetType().Name ?? ((int?)response?.StatusCode)?.ToString() ?? "unknown",
                delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// Reads the body under <paramref name="cancellationToken"/> and swaps in re-readable content.
    /// <c>HttpContent.LoadIntoBufferAsync</c> takes no cancellation token on this target framework, so
    /// the buffering is done by hand to keep it inside the attempt budget.
    /// </summary>
    private static async Task BufferAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var buffered = new ByteArrayContent(body);

        foreach (var header in response.Content.Headers)
        {
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content.Dispose();
        response.Content = buffered;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    /// <summary>
    /// A cancellation raised by the client's own timeout looks like a <see cref="TaskCanceledException"/>
    /// with an untriggered token; that is retryable, whereas caller cancellation is not.
    /// </summary>
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };

    private static TimeSpan DelayFor(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < MaxDelay ? delta : MaxDelay;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait < MaxDelay ? wait : MaxDelay;
            }
        }

        var backoff = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        if (backoff > MaxDelay.TotalMilliseconds)
        {
            backoff = MaxDelay.TotalMilliseconds;
        }

        // Equal jitter: half the backoff is always waited, so a throttled caller genuinely slows down,
        // and the other half is randomised so concurrent callers do not re-collide on the same slot.
        var half = backoff / 2;
        return TimeSpan.FromMilliseconds(half + Random.Shared.NextDouble() * half);
    }
}
