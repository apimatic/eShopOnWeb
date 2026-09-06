using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring <c>Retry-After</c>.
/// <para>
/// Rate limiting (429) is retried for every verb: Maxio rejected the call before doing any work, so a
/// replay cannot duplicate anything. Server errors and network faults are retried for safe verbs only -
/// a POST that times out may already have been applied, and re-sending it blindly could enroll a
/// shopper twice. Enrollment recovers from those instead by looking the subscription up by reference.
/// </para>
/// </summary>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly int _maxRetries;
    private readonly ILogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(int maxRetries, ILogger<MaxioTransientFaultHandler> logger)
    {
        _maxRetries = Math.Max(0, maxRetries);
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isSafeToReplay = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            TimeSpan delay;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests;
                var serverError = (int)response.StatusCode >= 500;

                if (attempt >= _maxRetries || !(rateLimited || (serverError && isSafeToReplay)))
                {
                    return response;
                }

                delay = ResolveDelay(response, attempt);
                _logger.LogWarning(
                    "Maxio request {Method} {Path} returned {StatusCode}; retrying in {Delay} (attempt {Attempt} of {MaxRetries}).",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, delay, attempt + 1, _maxRetries);

                response.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                       !cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();

                if (attempt >= _maxRetries || !isSafeToReplay)
                {
                    throw;
                }

                delay = ResolveDelay(null, attempt);
                _logger.LogWarning(
                    ex,
                    "Maxio request {Method} {Path} failed to complete; retrying in {Delay} (attempt {Attempt} of {MaxRetries}).",
                    request.Method, request.RequestUri?.AbsolutePath, delay, attempt + 1, _maxRetries);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan ResolveDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > MaxDelay ? MaxDelay : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until > MaxDelay ? MaxDelay : until;
            }
        }

        var backoff = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var total = backoff + jitter;
        return total > MaxDelay ? MaxDelay : total;
    }
}
