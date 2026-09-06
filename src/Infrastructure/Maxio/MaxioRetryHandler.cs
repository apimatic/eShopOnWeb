using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio calls that are safe to repeat: throttling (HTTP 429) for any request, and
/// transient server or transport failures for reads only. Writes such as
/// <c>POST /subscriptions.json</c> are never retried after the request reached Maxio, because a
/// response that never arrived may still have enrolled the shopper - the caller reconciles that
/// case by looking the subscription up instead.
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(20);

    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptions<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(0, _settings.Value.MaxRetryAttempts) + 1;
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!ShouldRetry(response.StatusCode, isRead) || isLastAttempt)
                {
                    return response;
                }

                var delay = GetDelay(attempt, response);
                _logger.LogWarning(
                    "Maxio call {Method} {Path} returned {StatusCode}; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                                       (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                response?.Dispose();

                if (!isRead || isLastAttempt)
                {
                    throw;
                }

                var delay = GetDelay(attempt, null);
                _logger.LogWarning(
                    ex,
                    "Maxio call {Method} {Path} failed to complete; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method.Method, request.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRead)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            // Throttled requests were not processed, so repeating them is safe for any method.
            return true;
        }

        return isRead && (int)statusCode >= 500;
    }

    /// <summary>
    /// Honours a <c>Retry-After</c> header when Maxio sends one, otherwise backs off exponentially
    /// with jitter so concurrent callers do not retry in lockstep.
    /// </summary>
    private static TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
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
        }

        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return Min(exponential + jitter, MaxDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
