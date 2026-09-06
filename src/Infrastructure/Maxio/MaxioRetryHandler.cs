using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio failures with exponential backoff and jitter, honouring Retry-After.
/// </summary>
/// <remarks>
/// Writes are retried as well as reads. That is safe here because every record this integration
/// creates carries a caller-assigned, deterministic <c>reference</c>, and Maxio enforces
/// uniqueness on it: if a retried POST duplicates one that actually succeeded, the retry is
/// rejected with "Reference: must be unique" and
/// <see cref="MaxioSubscriptionBillingService"/> resolves it to the record that already exists.
/// </remarks>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioRetryHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _settings.CurrentValue.MaxRetries + 1);

        // Buffer any request body up front so the request can be replayed on a retry.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();
            response = null;

            Exception? transientFailure = null;

            using var attemptRequest = CloneRequest(request, body);

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);

                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException ex)
            {
                transientFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout elapsed rather than the caller cancelling.
                transientFailure = ex;
            }

            if (attempt >= maxAttempts)
            {
                if (transientFailure is not null)
                {
                    throw transientFailure;
                }

                return response!;
            }

            var delay = GetDelay(response, attempt);

            _logger.LogWarning(
                transientFailure,
                "Maxio request {Method} {Path} failed transiently ({Outcome}) on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri?.AbsolutePath,
                response is null ? "network error" : ((int)response.StatusCode).ToString(),
                attempt,
                maxAttempts,
                (int)delay.TotalMilliseconds);

            response?.Dispose();
            response = null;

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return Min(delta, MaxDelay);
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return Min(until, MaxDelay);
            }
        }

        // Exponential backoff with full jitter, so retries from concurrent callers spread out.
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var capped = Min(exponential, MaxDelay);
        var jittered = Random.Shared.NextDouble() * capped.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(Math.Max(BaseDelay.TotalMilliseconds, jittered));
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source, byte[]? body)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (KeyValuePair<string, object?> option in source.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);

            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
