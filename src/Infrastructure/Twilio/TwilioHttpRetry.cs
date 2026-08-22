using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttpRetry
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        bool retryOnServerError,
        CancellationToken cancellationToken)
    {
        byte[]? contentBytes = null;
        MediaTypeHeaderValues contentType = MediaTypeHeaderValues.Capture(request);

        if (request.Content is not null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        HttpResponseMessage? response = null;
        Exception? lastException = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            response?.Dispose();

            var attemptRequest = CreateAttempt(request, contentBytes, contentType);
            try
            {
                response = await client.SendAsync(attemptRequest, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException("The provider request timed out.");
                await DelayAsync(attempt, retryAfter: null, cancellationToken);
                continue;
            }

            var retryable = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                            (retryOnServerError && (int)response.StatusCode is >= 500 and <= 599);

            if (retryable && attempt < 3)
            {
                TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
                await DelayAsync(attempt, retryAfter, cancellationToken);
                continue;
            }

            return response;
        }

        if (response is not null)
        {
            return response;
        }

        throw lastException ?? new HttpRequestException("The provider request failed without a response.");
    }

    private static HttpRequestMessage CreateAttempt(HttpRequestMessage template, byte[]? contentBytes, MediaTypeHeaderValues contentType)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri)
        {
            Version = template.Version
        };

        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            if (contentType.ContentType is not null)
            {
                clone.Content.Headers.TryAddWithoutValidation("Content-Type", contentType.ContentType);
            }
        }

        return clone;
    }

    private static async Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Min(30_000, 500 * Math.Pow(2, attempt))));
        await Task.Delay(delay, cancellationToken);
    }

    private readonly struct MediaTypeHeaderValues
    {
        public string? ContentType { get; init; }

        public static MediaTypeHeaderValues Capture(HttpRequestMessage request)
        {
            return new MediaTypeHeaderValues
            {
                ContentType = request.Content?.Headers.ContentType?.ToString()
            };
        }
    }
}
