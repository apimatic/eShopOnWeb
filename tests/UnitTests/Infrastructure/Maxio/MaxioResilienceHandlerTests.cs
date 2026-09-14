using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioResilienceHandlerTests
{
    [Fact]
    public async Task ThrottledReadsAreRetried()
    {
        // Maxio throttles by concurrency and asks callers to slow down rather than fan out, so a 429
        // is a "wait and try again", not a failure to surface.
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Send(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsAreRetried()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Send(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TransportFailuresAreRetried()
    {
        var stub = new StubHttpMessageHandler()
            .Throw(new HttpRequestException("connection reset"))
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Send(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RetriesAreBoundedAndTheLastResponseIsReturned()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.TooManyRequests);

        var response = await Send(stub, HttpMethod.Get, maxRetries: 2);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task WritesWithoutAUniquenessTokenAreNotReplayed()
    {
        // Replaying a write that carries no idempotency guarantee could enroll a shopper twice.
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.BadGateway);

        var response = await Send(stub, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task WritesMarkedRetrySafeAreReplayed()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.Created, "{}");

        var response = await Send(stub, HttpMethod.Post, retrySafe: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task ClientErrorsAreNotRetried()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.UnprocessableEntity);

        var response = await Send(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ARetryAfterHeaderIsHonoured()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests, "",
                response => response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(300)))
            .Respond(HttpStatusCode.OK, "{}");

        var started = DateTimeOffset.UtcNow;
        var response = await Send(stub, HttpMethod.Get, baseDelay: TimeSpan.FromMilliseconds(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(250),
            "the server-supplied Retry-After delay should win over the local backoff");
    }

    private static async Task<HttpResponseMessage> Send(
        StubHttpMessageHandler stub,
        HttpMethod method,
        int maxRetries = 3,
        bool retrySafe = false,
        TimeSpan? baseDelay = null)
    {
        var handler = new MaxioResilienceHandler(
            NullLogger<MaxioResilienceHandler>.Instance,
            maxRetries,
            baseDelay ?? TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5))
        {
            InnerHandler = stub
        };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };

        var request = new HttpRequestMessage(method, "subscriptions.json");
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        if (retrySafe)
        {
            request.Options.Set(MaxioResilienceHandler.RetrySafeOption, true);
        }

        return await client.SendAsync(request);
    }
}
