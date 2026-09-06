using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries throttled and transient-server responses with exponential backoff and jitter, honouring
/// a <c>Retry-After</c> header when Maxio sends one.
/// </summary>
/// <remarks>
/// Only requests that are safe to repeat are retried. A POST is never retried here even on a
/// timeout, because a subscription create that timed out may still have succeeded at Maxio;
/// duplicate-safety for those calls comes from the unique reference and the re-read that follows a
/// rejection, not from blind repetition.
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

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
        var maxAttempts = IsRepeatable(request.Method) ? Math.Max(0, settings.MaxRetryAttempts) : 0;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!IsTransient(response.StatusCode) || attempt >= maxAttempts)
                {
                    return response;
                }
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                // A cancellation that the caller did not ask for is the client-side timeout firing.
                transportFailure = ex;
            }

            var delay = transportFailure is null
                ? GetDelay(response!, attempt, settings)
                : GetBackoff(attempt, settings);

            _logger.LogWarning(
                "Maxio {Method} {Path} attempt {Attempt} of {MaxAttempts} failed ({Reason}); retrying in {DelayMs} ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                attempt + 1,
                maxAttempts + 1,
                transportFailure?.GetType().Name ?? ((int)response!.StatusCode).ToString(CultureInfo.InvariantCulture),
                (int)delay.TotalMilliseconds);

            response?.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRepeatable(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private static TimeSpan GetDelay(HttpResponseMessage response, int attempt, MaxioSettings settings)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxBackoff);
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return Min(wait, MaxBackoff);
            }
        }

        return GetBackoff(attempt, settings);
    }

    private static TimeSpan GetBackoff(int attempt, MaxioSettings settings)
    {
        var baseDelay = Math.Max(1, settings.RetryBaseDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * baseDelay;

        return Min(TimeSpan.FromMilliseconds(exponential + jitter), MaxBackoff);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
