using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential back-off and jitter.
/// </summary>
/// <remarks>
/// Maxio rate-limits each site to a fixed number of requests per hour and answers 429 once the
/// budget is exhausted; a 429 means the request was rejected, so it is safe to retry for any
/// method and the <c>Retry-After</c> header is honoured when present. Server errors and transport
/// faults are only retried for GET: a POST that failed after Maxio began processing it could
/// otherwise create a second customer or subscription.
/// </remarks>
public class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _settings.CurrentValue.MaxRetryAttempts);
        var isIdempotent = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!ShouldRetry(response.StatusCode, isIdempotent) || isLastAttempt)
                {
                    return response;
                }

                var delay = GetRetryAfter(response) ?? BackOff(attempt);
                _logger.LogWarning(
                    "Maxio {Method} {RequestUri} returned {StatusCode}; retrying in {Delay} (attempt {Attempt} of {MaxAttempts}).",
                    request.Method, request.RequestUri, (int)response.StatusCode, delay, attempt, maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransportFault(ex, cancellationToken) && isIdempotent && !isLastAttempt)
            {
                response?.Dispose();

                var delay = BackOff(attempt);
                _logger.LogWarning(
                    ex,
                    "Maxio {Method} {RequestUri} failed to complete; retrying in {Delay} (attempt {Attempt} of {MaxAttempts}).",
                    request.Method, request.RequestUri, delay, attempt, maxAttempts);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isIdempotent)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return isIdempotent && (int)statusCode >= 500;
    }

    private static bool IsTransportFault(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxDelay);
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Min(wait, MaxDelay);
            }
        }

        return null;
    }

    private static TimeSpan BackOff(int attempt)
    {
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return Min(exponential + jitter, MaxDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
