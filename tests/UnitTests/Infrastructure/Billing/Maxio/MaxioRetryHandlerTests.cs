using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioRetryHandlerTests
{
    private readonly StubHttpMessageHandler _handler = new();

    private HttpClient CreateClient() =>
        new(new MaxioRetryHandler(NullLogger<MaxioRetryHandler>.Instance) { InnerHandler = _handler });

    [Fact]
    public async Task RetriesAReadThatFailedTransiently()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, "[]");

        var response = await CreateClient().GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterThreeAttempts()
    {
        _handler.Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway);

        var response = await CreateClient().GetAsync("https://acme.chargify.com/site.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRepeatAWriteThatMayHaveBeenApplied()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable);

        var response = await CreateClient().PostAsync("https://acme.chargify.com/subscriptions.json",
            new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task RepeatsAThrottledWriteBecauseItWasNeverProcessed()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Created, """{ "subscription": { "id": 7 } }""");

        var response = await CreateClient().PostAsync("https://acme.chargify.com/subscriptions.json",
            new StringContent("""{"subscription":{"product_handle":"eshop-pro"}}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, _handler.Requests.Count);

        // The retry carries the same body as the original request.
        Assert.Equal(_handler.RequestBodies[0], _handler.RequestBodies[1]);
    }
}
