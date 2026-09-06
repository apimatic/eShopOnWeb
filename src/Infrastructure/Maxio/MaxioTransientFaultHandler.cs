using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and full jitter, honouring
/// <c>Retry-After</c> when the API rate-limits us (the specification documents 429 responses such as
/// "Too many requests. You can perform 5 requests within 00:30:00").
/// </summary>
/// <remarks>
/// GET is retried on any 5xx. POST is only retried on statuses that mean the request never reached
/// the application - 429 and the gateway-level 502/503/504 - because a 500 may well have created the
/// record. Even then, retries are safe: every record this integration creates carries a site-unique
/// <c>reference</c>, so a duplicate create is rejected with 422 and resolved by looking the record up.
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioTransientFaultHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _settings.CurrentValue.MaxRetryAttempts);

        if (maxRetries > 0 && request.Content is not null)
        {
            // The same content instance is sent again on retry, so it has to be buffered first.
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (attempt >= maxRetries || !IsRetryableStatus(request.Method, response.StatusCode))
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                if (attempt >= maxRetries)
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = response is not null
                ? GetDelay(attempt, response.Headers.RetryAfter)
                : GetDelay(attempt, retryAfter: null);

            _logger.LogWarning(
                "Retrying Maxio {Method} {Path} in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}). Reason: {Reason}",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                (int)delay.TotalMilliseconds,
                attempt + 1,
                maxRetries,
                response is not null ? $"HTTP {(int)response.StatusCode}" : transportFailure?.GetType().Name);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryableStatus(HttpMethod method, HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        return (int)statusCode >= 500 && IsIdempotent(method);
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static TimeSpan GetDelay(int attempt, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is not null)
        {
            var serverDelay = retryAfter.Delta ??
                              (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : null);

            if (serverDelay is { } delta && delta > TimeSpan.Zero)
            {
                return delta > MaxDelay ? MaxDelay : delta;
            }
        }

        var exponential = Math.Min(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * exponential);
    }
}
