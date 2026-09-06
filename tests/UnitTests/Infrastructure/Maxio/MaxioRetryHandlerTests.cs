using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    [Fact]
    public async Task RetriesAThrottledReadUntilItSucceeds()
    {
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.OK });
        var client = BuildClient(responses, out var attempts);

        var response = await client.GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task RetriesAFailedRead()
    {
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.BadGateway, HttpStatusCode.OK });
        var client = BuildClient(responses, out var attempts);

        var response = await client.GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task RetriesAThrottledWriteBecauseMaxioDidNotProcessIt()
    {
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.Created });
        var client = BuildClient(responses, out var attempts);

        var response = await client.PostAsync("subscriptions.json", new StringContent("{\"subscription\":{}}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, attempts.Count);

        // The body has to survive the retry, otherwise Maxio gets an empty create.
        Assert.All(attempts, body => Assert.Equal("{\"subscription\":{}}", body));
    }

    [Fact]
    public async Task DoesNotRetryAWriteThatFailedServerSide()
    {
        // A 5xx on a create may mean the subscription exists anyway; reissuing it would risk a
        // second one. Recovery belongs to the caller, which can go and look.
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.InternalServerError, HttpStatusCode.Created });
        var client = BuildClient(responses, out var attempts);

        var response = await client.PostAsync("subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(attempts);
    }

    [Fact]
    public async Task GivesUpAfterThreeAttempts()
    {
        var responses = new Queue<HttpStatusCode>();
        var client = BuildClient(responses, out var attempts, fallback: HttpStatusCode.TooManyRequests);

        var response = await client.GetAsync("site.json");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, attempts.Count);
    }

    [Fact]
    public async Task DoesNotRetryASuccessfulCall()
    {
        var responses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.OK });
        var client = BuildClient(responses, out var attempts);

        await client.GetAsync("site.json");

        Assert.Single(attempts);
    }

    private static HttpClient BuildClient(Queue<HttpStatusCode> responses, out List<string> attempts, HttpStatusCode fallback = HttpStatusCode.OK)
    {
        var recorded = new List<string>();
        attempts = recorded;

        var inner = new SequenceHandler(responses, recorded, fallback);
        var retry = new MaxioRetryHandler(NullLogger<MaxioRetryHandler>.Instance) { InnerHandler = inner };

        return new HttpClient(retry) { BaseAddress = new Uri("https://test-site.chargify.com/") };
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses;
        private readonly List<string> _attempts;
        private readonly HttpStatusCode _fallback;

        public SequenceHandler(Queue<HttpStatusCode> responses, List<string> attempts, HttpStatusCode fallback)
        {
            _responses = responses;
            _attempts = attempts;
            _fallback = fallback;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempts.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var status = _responses.Count > 0 ? _responses.Dequeue() : _fallback;
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}
