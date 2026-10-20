using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

public class UnknownOutcomeTests
{
    private static readonly BillingUser DemoUser = new("demouser@microsoft.com", "demouser@microsoft.com");
    private static readonly string DemoReference = SubscriptionService.CustomerReferenceFor(DemoUser.UserName);

    [Fact]
    public async Task CreateSubscriptionConnectionDropAfterMaxioActedIsSettledByReferenceLookup()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnSync(HttpMethod.Post, "/subscriptions.json", (_, body) =>
        {
            fake.CreateSubscriptionFrom(body);                         // Maxio acted ...
            throw new HttpRequestException("connection reset by peer"); // ... but the response never arrived
        });

        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(fake.Subscriptions.Single().Id, result.Subscription!.Id);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));       // never resent
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Get, "/subscriptions/lookup.json")); // settled by re-read
        var claim = await new BillingClaimStore(db).FindSubscriptionClaimAsync(DemoReference);
        Assert.Equal(BillingClaimStatus.Completed, claim!.Status);
        Assert.Equal(result.Subscription.Id, claim.ProviderSubscriptionId);
        db.Dispose();
    }

    [Fact]
    public async Task CreateSubscriptionTimeoutAfterMaxioActedIsSettledByReferenceLookup()
    {
        var fake = new FakeMaxio
        {
            Policy = new MaxioCallPolicy
            {
                CallBudget = TimeSpan.FromSeconds(5),
                AttemptTimeout = TimeSpan.FromMilliseconds(300), // the SDK's per-attempt timeout fires
                MaxRetries = 0,
                RequestBudget = TimeSpan.FromSeconds(20),
            }
        };
        fake.Stub.On(HttpMethod.Post, "/subscriptions.json", async (_, body) =>
        {
            fake.CreateSubscriptionFrom(body);
            await Task.Delay(TimeSpan.FromSeconds(30)); // provider hangs after acting
            throw new InvalidOperationException("unreachable");
        });

        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);
        db.Dispose();

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(fake.Subscriptions.Single().Id, result.Subscription!.Id);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task CreateSubscriptionServerErrorIsSettledNotReportedAsFailure()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnSync(HttpMethod.Post, "/subscriptions.json", (_, body) =>
        {
            fake.CreateSubscriptionFrom(body);
            return MaxioStubHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });

        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);
        db.Dispose();

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task UnsettledOutcomeStaysPendingBlocksRetryInFlightThenIsSettledWhenStale()
    {
        var fake = new FakeMaxio();
        var failNext = true;
        fake.Stub.OnSync(HttpMethod.Post, "/subscriptions.json", (_, body) =>
        {
            if (failNext)
            {
                failNext = false;
                throw new HttpRequestException("connection refused"); // Maxio never received it
            }
            return fake.CreateSubscriptionFrom(body);
        });

        // 1st call: outcome unknown -> pending, claim kept.
        var (s1, db1) = fake.NewScope();
        var first = await s1.SubscribeAsync(DemoUser, "eshop-pro", default);
        db1.Dispose();
        Assert.Equal(SubscribeStatus.Pending, first.Status);
        Assert.NotNull(first.SubscriptionReference);

        // 2nd call inside the in-flight window: refused by the Pending claim, nothing sent.
        var (s2, db2) = fake.NewScope();
        await Assert.ThrowsAsync<DuplicateException>(() => s2.SubscribeAsync(DemoUser, "eshop-pro", default));
        db2.Dispose();
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));

        // 3rd call after the window: re-read proves it never landed, claim released, subscription created once.
        fake.Clock.Advance(TimeSpan.FromMinutes(2));
        var (s3, db3) = fake.NewScope();
        var third = await s3.SubscribeAsync(DemoUser, "eshop-pro", default);
        db3.Dispose();

        Assert.Equal(SubscribeStatus.Created, third.Status);
        Assert.Single(fake.Subscriptions);
        Assert.Equal(2, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task CreateCustomerConnectionDropAfterMaxioActedIsSettledByReferenceLookup()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnSync(HttpMethod.Post, "/customers.json", (_, body) =>
        {
            fake.CreateCustomerFrom(body);
            throw new HttpRequestException("connection reset by peer");
        });

        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Single(fake.Customers);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
        var link = await new BillingClaimStore(db).FindCustomerLinkAsync(DemoReference);
        Assert.Equal(BillingClaimStatus.Completed, link!.Status);
        Assert.Equal(fake.Customers[DemoReference], link.ProviderCustomerId);
        db.Dispose();
    }

    [Fact]
    public async Task CreateSubscriptionRefusalReleasesTheClaimAndSurfacesTheProviderMessage()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnJson(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
            """{"errors":["A payment profile is required"]}""");

        var (service, db) = fake.NewScope();
        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(DemoUser, "eshop-pro", default));

        Assert.Equal(422, ex.HttpStatusCode);
        Assert.Contains("A payment profile is required", ex.Message);
        Assert.Null(await new BillingClaimStore(db).FindSubscriptionClaimAsync(DemoReference)); // released
        db.Dispose();
    }
}
