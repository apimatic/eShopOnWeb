using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient failures with exponential backoff and jitter. Maxio rate-limits at
/// 10,000 requests/hour/site and returns HTTP 429 without rate-limit headers, so backoff
/// must be implemented client-side. Retries 429 and 5xx responses and transient
/// network/timeout errors; leaves other 4xx alone since those are deterministic client
/// errors. A fresh <see cref="HttpRequestMessage"/> is built for every attempt because an
/// <see cref="HttpRequestMessage"/> (and its content stream) may only be sent once.
/// </summary>
public sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body once so it can be replayed on retries (JSON bodies are small).
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            using var attemptRequest = Clone(request, body);

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);

                if (attempt >= MaxRetries || !IsTransient(response.StatusCode))
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                // transient network failure — fall through to backoff
            }
            catch (TaskCanceledException) when (attempt < MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                // request timeout (not caller cancellation) — fall through to backoff
            }

            var delay = ComputeDelay(attempt);
            var status = response is null ? "network error" : $"HTTP {(int)response.StatusCode}";
            _logger.LogWarning(
                "Transient Maxio failure ({Status}) on {Method} {Path}; retrying in {Delay}ms (attempt {Attempt}/{Max}).",
                status, request.Method, request.RequestUri?.PathAndQuery, delay.TotalMilliseconds, attempt + 1, MaxRetries);

            response?.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage template, byte[]? body)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri)
        {
            Version = template.Version,
        };

        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (template.Content is not null)
            {
                foreach (var header in template.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan ComputeDelay(int attempt)
    {
        var backoff = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(backoff + jitter);
    }
}
