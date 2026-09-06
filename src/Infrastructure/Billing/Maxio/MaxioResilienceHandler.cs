using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Applies the two things Maxio's guidance asks of an API client: keep concurrency low, and back
/// off instead of hammering when throttled.
/// </summary>
/// <remarks>
/// <para>
/// Retries cover 429, 5xx and transport faults, with exponential backoff plus jitter and respect
/// for <c>Retry-After</c>. The timeout is applied per attempt rather than across the whole
/// pipeline, so a retry gets a full budget instead of the remains of the first attempt's.
/// </para>
/// <para>
/// POSTs are retried too, which is only safe because every write this integration issues is
/// guarded: subscription creates carry a <c>uniqueness_token</c>, and customer creates are
/// protected by the uniqueness Maxio enforces on the customer <c>reference</c>.
/// </para>
/// </remarks>
public sealed class MaxioResilienceHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(20);

    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly MaxioConcurrencyGate _gate;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(
        IOptionsMonitor<MaxioOptions> options,
        MaxioConcurrencyGate gate,
        ILogger<MaxioResilienceHandler> logger)
    {
        _options = options;
        _gate = gate;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var maxAttempts = Math.Max(0, options.MaxRetryAttempts) + 1;
        var perAttemptTimeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryBaseDelayMilliseconds));

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? failure = null;

            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptCts.CancelAfter(perAttemptTimeout);

                using var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                           && !cancellationToken.IsCancellationRequested)
                {
                    // An OperationCanceledException with the caller's token still live means this
                    // attempt hit its own timeout, which is retryable.
                    failure = ex;
                }
            }

            if (failure is null && !IsTransient(response!.StatusCode))
            {
                return response;
            }

            if (attempt >= maxAttempts)
            {
                if (failure is not null)
                {
                    throw failure is OperationCanceledException
                        ? new HttpRequestException(
                            $"Maxio did not respond to {request.Method} {request.RequestUri} within " +
                            $"{perAttemptTimeout.TotalSeconds:0}s across {attempt} attempt(s).", failure)
                        : failure;
                }

                _logger.LogWarning(
                    "Maxio {Method} {Uri} still returning {StatusCode} after {Attempts} attempt(s); giving up.",
                    request.Method, request.RequestUri, (int)response!.StatusCode, attempt);
                return response;
            }

            var delay = RetryAfter(response) ?? Backoff(baseDelay, attempt);

            _logger.LogWarning(
                failure,
                "Maxio {Method} {Uri} attempt {Attempt}/{MaxAttempts} failed ({Reason}); retrying in {Delay}.",
                request.Method,
                request.RequestUri,
                attempt,
                maxAttempts,
                failure?.GetType().Name ?? ((int)response!.StatusCode).ToString(),
                delay);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false
    };

    private static TimeSpan? RetryAfter(HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < MaxBackoff ? delta : MaxBackoff;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait < MaxBackoff ? wait : MaxBackoff;
            }
        }

        return null;
    }

    private static TimeSpan Backoff(TimeSpan baseDelay, int attempt)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, MaxBackoff.TotalMilliseconds));
    }
}
