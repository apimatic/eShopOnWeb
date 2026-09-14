using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring
/// <c>Retry-After</c> when the service sends one.
/// </summary>
/// <remarks>
/// Writes are retried as well as reads. That is safe here because every create eShopOnWeb issues
/// carries a reference the billing system enforces uniqueness on: if a retried create turns out to
/// have already succeeded, Maxio answers 422 and the caller resolves it by reading the record back
/// rather than producing a duplicate.
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(8);

    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioTransientFaultHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.CurrentValue.MaxRetryAttempts);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout elapsed rather than the caller cancelling.
                transportFailure = ex;
            }

            if (attempt >= maxRetries)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transportFailure!;
            }

            var delay = GetDelay(response, attempt);
            _logger.LogWarning(
                "Maxio call {Method} {Path} failed transiently ({Reason}); retrying in {DelayMs} ms (attempt {Attempt} of {MaxAttempts}).",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                response is not null ? ((int)response.StatusCode).ToString() : transportFailure?.GetType().Name,
                (int)delay.TotalMilliseconds,
                attempt + 1,
                maxRetries);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.InternalServerError ||
        statusCode == HttpStatusCode.BadGateway ||
        statusCode == HttpStatusCode.ServiceUnavailable ||
        statusCode == HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, _maxDelay);
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return Min(until, _maxDelay);
            }
        }

        var backoff = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return Min(backoff + jitter, _maxDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
