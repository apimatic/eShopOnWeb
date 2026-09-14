using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Applies the concurrency budget and retries transient failures on the way to the Maxio API.
/// </summary>
/// <remarks>
/// Retrying a POST is only safe because every write this integration issues is de-duplicated on the
/// server: customers carry a unique reference and subscriptions carry both a unique reference and a
/// uniqueness token, so a replay is rejected with HTTP 422 or 409 rather than duplicated. The
/// orchestration layer reconciles those two responses against the record that already exists.
/// </remarks>
public class MaxioResilienceHandler : DelegatingHandler
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly MaxioRequestThrottle _throttle;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(MaxioRequestThrottle throttle, ILogger<MaxioResilienceHandler> logger)
    {
        _throttle = throttle;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A request message can only be sent once, so the body is buffered up front and each attempt
        // gets a fresh message built from it.
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            var attemptRequest = CloneRequest(request, body);
            HttpResponseMessage? response = null;

            try
            {
                using (await _throttle.AcquireAsync(cancellationToken))
                {
                    response = await base.SendAsync(attemptRequest, cancellationToken);
                }
            }
            catch (Exception ex) when (IsTransientTransportFailure(ex, cancellationToken) && attempt < MaxRetryAttempts)
            {
                attemptRequest.Dispose();
                _logger.LogWarning(ex, "Maxio {Method} {Uri} failed to reach the API (attempt {Attempt} of {MaxAttempts}); retrying.",
                    request.Method.Method, request.RequestUri, attempt + 1, MaxRetryAttempts + 1);

                await Task.Delay(CalculateDelay(attempt, retryAfter: null), cancellationToken);
                continue;
            }
            catch
            {
                attemptRequest.Dispose();
                throw;
            }

            if (attempt < MaxRetryAttempts && ShouldRetry(response.StatusCode))
            {
                var delay = CalculateDelay(attempt, response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow));

                _logger.LogWarning("Maxio {Method} {Uri} responded {StatusCode} (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}.",
                    request.Method.Method, request.RequestUri, (int)response.StatusCode, attempt + 1, MaxRetryAttempts + 1, delay);

                response.Dispose();
                attemptRequest.Dispose();

                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }
    }

    /// <summary>
    /// 429 means the site's concurrency budget is exhausted and the guidance is to back off rather than
    /// push harder. 5xx and 408 are the usual transient server-side faults.
    /// </summary>
    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private static bool IsTransientTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException => true,
        // A TaskCanceledException that is not our own cancellation is the HttpClient timeout firing.
        TaskCanceledException or OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };

    private static TimeSpan CalculateDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait > MaxDelay ? MaxDelay : wait;
        }

        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        if (exponential > MaxDelay)
        {
            exponential = MaxDelay;
        }

        // Jitter keeps a burst of callers from retrying in lockstep and re-creating the pile-up.
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        return TimeSpan.FromMilliseconds(exponential.TotalMilliseconds * jitter);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
