using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and full jitter.
/// </summary>
/// <remarks>
/// Maxio limits callers by concurrency rather than request rate and answers overload with
/// <c>429</c>, so backing off (instead of parallelising harder) is the documented remedy.
/// Only requests marked retry-safe are replayed: a request whose replay could create a second
/// resource is attempted exactly once unless the provider never accepted it.
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    /// <summary>Marks a request whose replay is safe (naturally idempotent, or de-duplicated by the provider).</summary>
    internal static readonly HttpRequestOptionsKey<bool> RetrySafeOption = new("Maxio.RetrySafe");

    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8);

    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(ILogger<MaxioTransientFaultHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var replaySafe = IsReplaySafe(request);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                if (!ShouldRetryResponse(response, replaySafe) || attempt == MaxRetries)
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                // The provider never produced a response, so replaying cannot duplicate work.
                if (attempt == MaxRetries)
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = DelayFor(attempt, response);
            _logger.LogWarning(
                "Maxio request {Method} {Path} failed ({Outcome}); retrying in {DelayMs}ms (attempt {Attempt} of {MaxRetries}).",
                request.Method, request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                (int)delay.TotalMilliseconds, attempt + 1, MaxRetries);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsReplaySafe(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get ||
        (request.Options.TryGetValue(RetrySafeOption, out var flag) && flag);

    private static bool ShouldRetryResponse(HttpResponseMessage response, bool replaySafe)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Throttling means the request was shed, not processed.
            return true;
        }

        if (!replaySafe)
        {
            return false;
        }

        return response.StatusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            // HttpClient surfaces its own timeout as a cancellation that the caller did not request.
            TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };

    private static TimeSpan DelayFor(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            var hinted = retryAfter.Delta ??
                         (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);
            if (hinted is { } wait && wait > TimeSpan.Zero)
            {
                return wait > MaxDelay ? MaxDelay : wait;
            }
        }

        var window = Math.Min(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(100, Random.Shared.NextDouble() * window));
    }
}
