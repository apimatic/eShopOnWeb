using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

public class DuplicateClaimTests
{
    private static readonly BillingUser DemoUser = new("demouser@microsoft.com", "demouser@microsoft.com");
    private static readonly string DemoReference = SubscriptionService.CustomerReferenceFor(DemoUser.UserName);

    [Fact]
    public async Task StoreRefusesSecondClaimForSameKeyFromAnotherContext()
    {
        var name = "claims-" + Guid.NewGuid();
        using var first = NewContext(name);
        using var second = NewContext(name);
        var now = DateTimeOffset.UtcNow;

        Assert.True(await new BillingClaimStore(first).TryClaimSubscriptionAsync(new SubscriptionClaim("eshop:a", "ref-1", "eshop-pro", now)));
        Assert.False(await new BillingClaimStore(second).TryClaimSubscriptionAsync(new SubscriptionClaim("eshop:a", "ref-2", "eshop-pro", now)));

        Assert.True(await new BillingClaimStore(first).TryClaimCustomerAsync(new BillingCustomerLink("eshop:a", now)));
        Assert.False(await new BillingClaimStore(second).TryClaimCustomerAsync(new BillingCustomerLink("eshop:a", now)));
    }

    [Fact]
    public async Task OnlyOneOfManyConcurrentClaimsWins()
    {
        var name = "claims-" + Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            using var db = NewContext(name);
            return await new BillingClaimStore(db).TryClaimSubscriptionAsync(new SubscriptionClaim("eshop:race", $"ref-{i}", "eshop-pro", now));
        })));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task SequentialDoubleSubmitReturnsExistingAndCreatesOnlyOne()
    {
        var fake = new FakeMaxio();

        var (firstService, firstDb) = fake.NewScope();
        var first = await firstService.SubscribeAsync(DemoUser, "eshop-pro", default);
        firstDb.Dispose();

        var (secondService, secondDb) = fake.NewScope();
        var second = await secondService.SubscribeAsync(DemoUser, "eshop-pro", default);
        secondDb.Dispose();

        Assert.Equal(SubscribeStatus.Created, first.Status);
        Assert.Equal(SubscribeStatus.Existing, second.Status);
        Assert.Equal(first.Subscription!.Id, second.Subscription!.Id);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentSubscribeIsRefusedByTheSubscriptionClaimBeforeReachingMaxio()
    {
        var fake = new FakeMaxio();
        // The customer already exists and is linked, so both callers race for the subscription claim.
        var customerId = fake.AddCustomer(DemoReference);
        using (var seed = fake.NewScope().Db)
        {
            var link = new BillingCustomerLink(DemoReference, fake.Clock.GetUtcNow());
            link.Complete(customerId, fake.Clock.GetUtcNow());
            seed.BillingCustomerLinks.Add(link);
            await seed.SaveChangesAsync();
        }

        var firstInFlight = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        fake.BeforeCreateSubscription = async () =>
        {
            firstInFlight.TrySetResult();
            await releaseFirst.Task;
        };

        var (firstService, firstDb) = fake.NewScope();
        var firstCall = firstService.SubscribeAsync(DemoUser, "eshop-pro", default);
        await firstInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var (secondService, secondDb) = fake.NewScope();
        var refused = await Assert.ThrowsAsync<DuplicateException>(() => secondService.SubscribeAsync(DemoUser, "eshop-pro", default));

        releaseFirst.SetResult();
        var first = await firstCall;
        firstDb.Dispose();
        secondDb.Dispose();

        Assert.Equal(SubscribeStatus.Created, first.Status);
        Assert.Contains("already being processed", refused.Message);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentFirstSubscribeIsRefusedByTheCustomerClaimBeforeReachingMaxio()
    {
        var fake = new FakeMaxio();
        var firstInFlight = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        fake.BeforeCreateCustomer = async () =>
        {
            firstInFlight.TrySetResult();
            await releaseFirst.Task;
        };

        var (firstService, firstDb) = fake.NewScope();
        var firstCall = firstService.SubscribeAsync(DemoUser, "eshop-pro", default);
        await firstInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var (secondService, secondDb) = fake.NewScope();
        await Assert.ThrowsAsync<DuplicateException>(() => secondService.SubscribeAsync(DemoUser, "eshop-pro", default));

        releaseFirst.SetResult();
        var first = await firstCall;
        firstDb.Dispose();
        secondDb.Dispose();

        Assert.Equal(SubscribeStatus.Created, first.Status);
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(fake.Customers);
    }

    [Fact]
    public async Task ExistingLiveSubscriptionAtMaxioIsAdoptedInsteadOfCreatingAnother()
    {
        // Local claims lost (e.g. in-memory store after a restart) but Maxio still holds the customer and subscription.
        var fake = new FakeMaxio();
        var customerId = fake.AddCustomer(DemoReference);
        var subscriptionId = fake.AddSubscription(customerId, "earlier-ref", "eshop-pro");

        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);
        db.Dispose();

        Assert.Equal(SubscribeStatus.Existing, result.Status);
        Assert.Equal(subscriptionId, result.Subscription!.Id);
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    private static CatalogContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(name).Options);
}
