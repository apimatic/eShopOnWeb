using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioResilienceHandlerTests
{
    private readonly MaxioSettings _settings = new()
    {
        MaxRetryAttempts = 2,
        RetryBaseDelayMilliseconds = 1
    };

    [Fact]
    public async Task SendAsync_RetriesWhenMaxioThrottlesAndReturnsTheEventualSuccess()
    {
        var inner = new ScriptedHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get, body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_RetriesServerErrorsUpToTheConfiguredLimitAndThenReturnsTheLastResponse()
    {
        var inner = new ScriptedHandler(
            HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Get, body: null);

        // Two retries after the first attempt, so the fourth (successful) response is never reached.
        Assert.Equal(3, inner.Attempts);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryAClientError()
    {
        var inner = new ScriptedHandler(HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK);

        var response = await SendAsync(inner, HttpMethod.Post, body: "{}");

        Assert.Equal(1, inner.Attempts);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ResendsTheFullBodyOnEveryRetry()
    {
        var inner = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        await SendAsync(inner, HttpMethod.Post, """{"subscription":{"product_handle":"eshop-pro"}}""");

        Assert.Equal(2, inner.Attempts);
        Assert.Equal(2, inner.Bodies.Count);
        Assert.All(inner.Bodies, body => Assert.Equal("""{"subscription":{"product_handle":"eshop-pro"}}""", body));
        Assert.All(inner.ContentTypes, contentType => Assert.Equal("application/json", contentType));
    }

    [Fact]
    public async Task SendAsync_RetriesATransportFailure()
    {
        var inner = new ScriptedHandler(HttpStatusCode.OK) { FailFirstAttemptsWithTransportError = 1 };

        var response = await SendAsync(inner, HttpMethod.Get, body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_GivesUpOnATransportFailureThatNeverRecovers()
    {
        var inner = new ScriptedHandler(HttpStatusCode.OK) { FailFirstAttemptsWithTransportError = 99 };

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(inner, HttpMethod.Get, body: null));

        Assert.Equal(3, inner.Attempts);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMessageHandler inner, HttpMethod method, string? body)
    {
        var handler = new MaxioResilienceHandler(_settings, NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        using var request = new HttpRequestMessage(method, "subscriptions.json");

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _statusCodes;

        public ScriptedHandler(params HttpStatusCode[] statusCodes) => _statusCodes = statusCodes;

        public int Attempts { get; private set; }

        public int FailFirstAttemptsWithTransportError { get; set; }

        public List<string> Bodies { get; } = new();

        public List<string?> ContentTypes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;

            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
                ContentTypes.Add(request.Content.Headers.ContentType?.MediaType);
            }

            if (Attempts <= FailFirstAttemptsWithTransportError)
            {
                throw new HttpRequestException("connection reset");
            }

            return new HttpResponseMessage(_statusCodes[Math.Min(Attempts - 1, _statusCodes.Length - 1)]);
        }
    }
}
