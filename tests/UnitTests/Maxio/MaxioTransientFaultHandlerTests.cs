using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioTransientFaultHandlerTests
{
    [Fact]
    public async Task ThrottledReadsAreRetried()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.TooManyRequests, "Your request was denied due to a usage violation.")
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)));

        var plans = await MaxioTestHost.CreateService(transport).ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsOnReadsAreRetried()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.BadGateway)
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)));

        var plans = await MaxioTestHost.CreateService(transport).ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task PersistentFailuresAreGivenUpOnAndReported()
    {
        var transport = new StubHttpMessageHandler();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            transport.RespondWith(HttpStatusCode.ServiceUnavailable);
        }

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            MaxioTestHost.CreateService(transport).ListPlansAsync());

        Assert.Equal(503, exception.StatusCode);
        // One attempt plus three retries, and no more: retrying forever would only deepen the queue.
        Assert.Equal(4, transport.Requests.Count);
    }

    [Fact]
    public async Task SignupsAreReplayedBecauseTheUniquenessTokenMakesADuplicateDetectable()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.ServiceUnavailable)
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211938, "active", "pro-plan"));

        var result = await MaxioTestHost.CreateService(transport).SubscribeAsync(
            new SubscribeRequest(
                new BillingCustomerProfile("shopper@example.com", "shopper@example.com"),
                "pro-plan",
                idempotencyKey: "retry-me"));

        Assert.True(result.Created);

        var signups = transport.Requests
            .Where(request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/subscriptions.json")
            .ToList();
        Assert.Equal(2, signups.Count);
        // The replay must carry the same token, or Maxio could not recognise it as a duplicate.
        Assert.All(signups, signup => Assert.Contains("\"uniqueness_token\":\"retry-me\"", signup.Body));
    }

    [Fact]
    public async Task WritesWithoutDuplicateProtectionAreNotReplayed()
    {
        // A customer create carries no uniqueness token, so a 500 might mean the customer exists.
        // Replaying it could create a second one, so it is attempted exactly once.
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.InternalServerError, MaxioPayloads.Errors("Something went wrong."));

        var client = MaxioTestHost.CreateClient(transport);

        await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.CreateCustomerAsync(
                new MaxioCustomerAttributes
                {
                    Email = "shopper@example.com",
                    FirstName = "Shopper",
                    LastName = "eShopOnWeb",
                    Reference = "eshop-shopper@example.com"
                },
                CancellationToken.None));

        Assert.Single(transport.Requests);
    }
}
