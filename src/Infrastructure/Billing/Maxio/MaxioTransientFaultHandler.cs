using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Applies a per-attempt timeout to Maxio calls and retries transient failures with exponential
/// backoff and full jitter.
/// </summary>
/// <remarks>
/// <para>
/// The timeout lives here rather than on <see cref="HttpClient.Timeout"/> because that one budget
/// would have to cover every retry and every backoff delay, so one slow attempt would starve the
/// rest. Each attempt instead gets the configured timeout of its own.
/// </para>
/// <para>
/// Advanced Billing rate limits per site and answers 429 once the limit is exceeded, but returns
/// neither rate-limit nor <c>Retry-After</c> headers, so the backoff has to be driven entirely from
/// the client. A <c>Retry-After</c> header is still honoured if one ever appears.
/// </para>
/// <para>
/// Retrying is safe for every request this integration makes, including the two POSTs. Both carry a
/// caller-computed <c>reference</c> whose uniqueness Maxio enforces, so a retry of a create that in
/// fact succeeded upstream comes back as a 422 that the calling service resolves by looking the
/// existing record up. A retry cannot produce a duplicate.
/// </para>
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioTransientFaultHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var maxAttempts = Math.Max(1, settings.MaxRetryAttempts + 1);
        var attemptTimeout = settings.Timeout > TimeSpan.Zero ? settings.Timeout : TimeSpan.FromSeconds(30);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? failure = null;

            using (var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptCancellation.CancelAfter(attemptTimeout);

                try
                {
                    response = await base.SendAsync(request, attemptCancellation.Token).ConfigureAwait(false);

                    // Buffer while the attempt's cancellation is still armed: the caller reads the body
                    // after this handler returns, and that read must not outlive the attempt timeout.
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);

                    if (!IsTransient(response.StatusCode) || attempt >= maxAttempts)
                    {
                        return response;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    response?.Dispose();
                    throw;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    response?.Dispose();
                    response = null;

                    if (attempt >= maxAttempts)
                    {
                        throw new HttpRequestException(
                            $"Maxio {request.Method} {request.RequestUri?.AbsolutePath} failed after {maxAttempts} attempt(s).", ex);
                    }

                    failure = ex;
                }
            }

            var delay = NextDelay(settings, attempt, response);

            _logger.LogWarning(
                "Maxio {Method} {Path} failed transiently ({Outcome}); retrying in {DelayMs} ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : failure?.GetType().Name,
                (int)delay.TotalMilliseconds,
                attempt,
                maxAttempts);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static bool IsTransient(Exception exception) => exception switch
    {
        // Reached only when the caller's own token is not the one that fired, so this is the
        // per-attempt timeout.
        OperationCanceledException => true,
        HttpRequestException => true,
        _ => false
    };

    private static TimeSpan NextDelay(MaxioSettings settings, int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
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

        var baseDelay = settings.RetryBaseDelay > TimeSpan.Zero ? settings.RetryBaseDelay : TimeSpan.FromMilliseconds(500);
        var window = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);

        // Full jitter: spread retries across the window so concurrent callers do not resynchronise.
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * window);
    }
}
