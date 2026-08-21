using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries Maxio 429 responses with backoff. Billing API rate-limits by concurrency and asks
/// callers to pause rather than fire more parallel work.
/// </summary>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 4;
    private readonly ILogger<MaxioRetryHandler> _logger;

    public MaxioRetryHandler(ILogger<MaxioRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? contentBytes = null;
        MediaTypeHeaderValue? contentType = null;
        if (request.Content is not null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType;
        }

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            var message = Clone(request, contentBytes, contentType);
            response = await base.SendAsync(message, cancellationToken);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            _logger.LogWarning(
                "Maxio returned 429 for {Method} {Path}; retrying in {Delay}s (attempt {Attempt}/{Max}).",
                request.Method, request.RequestUri?.PathAndQuery, delay.TotalSeconds, attempt, MaxAttempts);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? contentBytes, MediaTypeHeaderValue? contentType)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            if (contentType is not null)
            {
                clone.Content.Headers.ContentType = contentType;
            }
        }

        return clone;
    }
}
