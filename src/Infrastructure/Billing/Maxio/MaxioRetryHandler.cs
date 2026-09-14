using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries Maxio calls that failed for a reason that is likely to pass.
/// <para>
/// The policy is deliberately asymmetric. Reads are replayed on throttling, transient server
/// errors and connection failures. Writes are replayed only on throttling (HTTP 429), where Maxio
/// has told us it did not process the request: a 5xx or a dropped connection on
/// <c>POST /subscriptions.json</c> may well have created the subscription, and re-sending it
/// blindly is how a shopper ends up billed twice. The subscription-level reference gives us a
/// safe way to recover from those instead, so this handler leaves them alone.
/// </para>
/// </summary>
public class MaxioRetryHandler : DelegatingHandler
{
    private const int TooManyRequests = 429;

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
        var maxAttempts = Math.Max(1, settings.MaxRetryAttempts);
        var baseDelay = TimeSpan.FromMilliseconds(Math.Max(1, settings.RetryBaseDelayMilliseconds));
        var isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= maxAttempts;

            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (isLastAttempt || !ShouldRetry(response.StatusCode, isRead))
                {
                    return response;
                }

                var delay = DelayFor(response, attempt, baseDelay);

                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode}; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException exception) when (isRead && !isLastAttempt)
            {
                var delay = Backoff(attempt, baseDelay);

                _logger.LogWarning(exception,
                    "Maxio {Method} {Path} could not be reached; retrying in {Delay}ms (attempt {Attempt} of {MaxAttempts}).",
                    request.Method, request.RequestUri?.AbsolutePath,
                    (int)delay.TotalMilliseconds, attempt, maxAttempts);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, bool isRead)
    {
        if ((int)statusCode == TooManyRequests)
        {
            return true;
        }

        return isRead && (int)statusCode >= 500;
    }

    private static TimeSpan DelayFor(HttpResponseMessage response, int attempt, TimeSpan baseDelay)
    {
        // Advanced Billing does not document a Retry-After on its throttling responses, but
        // honour one whenever it is there rather than guessing over the top of it.
        var retryAfter = response.Headers.RetryAfter;

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

        return Backoff(attempt, baseDelay);
    }

    /// <summary>Exponential backoff with jitter, so concurrent callers do not retry in lockstep.</summary>
    private static TimeSpan Backoff(int attempt, TimeSpan baseDelay)
    {
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, 10_000));
    }
}
