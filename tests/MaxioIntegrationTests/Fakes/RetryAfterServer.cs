using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Throttles the first attempt with a Retry-After header, then serves the request.</summary>
internal sealed class RetryAfterServer : HttpMessageHandler
{
    private readonly TimeSpan _retryAfter;

    public RetryAfterServer(TimeSpan retryAfter)
    {
        _retryAfter = retryAfter;
    }

    public int Attempts { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attempts++;

        if (Attempts > 1)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        }

        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(_retryAfter);

        return Task.FromResult(throttled);
    }
}
