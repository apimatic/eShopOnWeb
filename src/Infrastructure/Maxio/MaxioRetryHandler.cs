using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Retries transient Maxio responses (429, 5xx). Request content is buffered so POST bodies can be resent.
/// </summary>
internal sealed class MaxioRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1600)
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= Delays.Length; attempt++)
        {
            response?.Dispose();
            response = await base.SendAsync(request, cancellationToken);

            if (!ShouldRetry(response.StatusCode) || attempt == Delays.Length)
            {
                return response;
            }

            var delay = Delays[attempt];
            if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > delay)
            {
                delay = retryAfter;
            }

            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 429 || code >= 500;
    }
}
