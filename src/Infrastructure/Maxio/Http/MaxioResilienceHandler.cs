using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Retries throttled, transient and transport failures with exponential backoff and jitter,
/// honouring a Retry-After header when Maxio sends one.
/// </summary>
/// <remarks>
/// Retrying a POST is safe here because every write this integration issues carries a
/// <c>uniqueness_token</c>: Maxio answers a repeated submission with 409 rather than performing it
/// twice, and the billing service resolves that 409 by re-reading. The whole retry sequence runs
/// inside the HttpClient timeout, so total latency stays bounded.
/// </remarks>
public sealed class MaxioResilienceHandler : DelegatingHandler
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioResilienceHandler> _logger;

    public MaxioResilienceHandler(MaxioSettings settings, ILogger<MaxioResilienceHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var maxAttempts = Math.Max(0, _settings.MaxRetryAttempts) + 1;

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                response = await base.SendAsync(CloneRequest(request, body), cancellationToken);

                if (attempt >= maxAttempts || !IsRetryable(response.StatusCode))
                {
                    return response;
                }
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt >= maxAttempts)
                {
                    throw;
                }

                transportFailure = ex;
            }

            var delay = GetDelay(attempt, response);

            if (transportFailure is not null)
            {
                _logger.LogWarning(transportFailure,
                    "Maxio {Method} {Path} failed to send (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.",
                    request.Method, request.RequestUri?.AbsolutePath, attempt, maxAttempts, delay);
            }
            else
            {
                _logger.LogWarning(
                    "Maxio {Method} {Path} returned {StatusCode} (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response!.StatusCode, attempt, maxAttempts, delay);
            }

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// 429 is Maxio's throttling signal; 408 and 5xx are server-side failures worth one more try.
    /// </summary>
    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    /// <summary>
    /// Cancellation - including the HttpClient timeout expiring - is deliberately not transient:
    /// the time budget for the whole call is already gone, so retrying would only add latency.
    /// </summary>
    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException;

    private TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var untilDate = date - DateTimeOffset.UtcNow;
            if (untilDate > TimeSpan.Zero)
            {
                return untilDate;
            }
        }

        var baseDelay = Math.Max(1, _settings.RetryBaseDelayMilliseconds);
        var backoff = baseDelay * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * baseDelay;
        return TimeSpan.FromMilliseconds(backoff + jitter);
    }

    /// <summary>
    /// A request message can only be sent once, so each attempt gets a fresh copy built from the
    /// body buffered up front.
    /// </summary>
    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? body)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in original.Options)
        {
            ((System.Collections.Generic.IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (original.Content is not null)
            {
                foreach (var header in original.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
