using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Retries Maxio 429 (and transient 5xx GET) responses with exponential backoff.
/// POSTs are only retried on 429 so a uniqueness_token can make the replay safe.
/// </summary>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 4;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            response = await base.SendAsync(request, cancellationToken);

            if (!ShouldRetry(request, response, attempt))
            {
                return response;
            }

            var delay = GetDelay(response, attempt);
            _logger.LogWarning(
                "Maxio request {Method} {Path} returned {Status}; retrying in {Delay} (attempt {Attempt}/{Max})",
                request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode, delay, attempt, MaxAttempts);

            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static bool ShouldRetry(HttpRequestMessage request, HttpResponseMessage response, int attempt)
    {
        if (attempt >= MaxAttempts)
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (request.Method == HttpMethod.Get && (int)response.StatusCode is >= 500 and <= 599)
        {
            return true;
        }

        return false;
    }

    private static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        var seconds = Math.Min(Math.Pow(2, attempt), 30);
        return TimeSpan.FromSeconds(seconds);
    }
}
