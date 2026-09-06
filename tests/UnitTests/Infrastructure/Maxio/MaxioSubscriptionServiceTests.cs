using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static readonly SubscriberIdentity Subscriber = new("demouser@microsoft.com");

    private static MaxioSubscriptionService Build(FakeMaxioApiClient client, MaxioOptions? options = null) =>
        new(client,
            new MemoryCache(new MemoryCacheOptions()),
            new SubscriberGate(),
            new StaticOptionsMonitor<MaxioOptions>(options ?? MaxioTestOptions.Valid()),
            NullLogger<MaxioSubscriptionService>.Instance);

    private static MaxioProduct Product(string handle, long priceInCents, DateTimeOffset? archivedAt = null) => new()
    {
        Id = handle.GetHashCode() & 0x7fffffff,
        Handle = handle,
        Name = handle,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt
    };

    // -- plans --------------------------------------------------------------------------------

    [Fact]
    public async Task GetPlansAsksForTheConfiguredFamilyByHandle()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var options = MaxioTestOptions.Valid();
        options.ProductFamilyHandle = "eshop-subscribe";

        await Build(client, options).GetPlansAsync();

        Assert.Equal("handle:eshop-subscribe", Assert.Single(client.RequestedProductFamilies));
    }

    [Fact]
    public async Task GetPlansHidesArchivedProductsAndOrdersByPrice()
    {
        var client = new FakeMaxioApiClient
        {
            Products =
            {
                Product("pro", 29900),
                Product("basic", 2900),
                Product("retired", 100, archivedAt: DateTimeOffset.UtcNow)
            }
        };

        var plans = await Build(client).GetPlansAsync();

        Assert.Equal(new[] { "basic", "pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal("299.00", plans[1].FormattedPrice);
    }

    [Fact]
    public async Task GetPlansExplainsAMisconfiguredProductFamily()
    {
        var client = new FakeMaxioApiClient
        {
            ListProductsFailure = new MaxioApiException("not found", HttpStatusCode.NotFound)
        };

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => Build(client).GetPlansAsync());

        Assert.Contains("ProductFamilyHandle", exception.Message);
    }

    // -- enrolment ----------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeCreatesTheBillingCustomerOnFirstUse()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };

        var result = await Build(client).SubscribeAsync(Subscriber, "pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("active", result.Subscription.State);

        var customer = Assert.Single(client.CreatedCustomers);
        Assert.Equal("eshoponweb:demouser@microsoft.com", customer.Reference);
        Assert.Equal("demouser@microsoft.com", customer.Email);
        // Maxio requires both names; eShopOnWeb accounts carry none, so they come from the e-mail.
        Assert.False(string.IsNullOrWhiteSpace(customer.FirstName));
        Assert.False(string.IsNullOrWhiteSpace(customer.LastName));
    }

    [Fact]
    public async Task SubscribeSendsTheConfiguredCollectionMethodAndThePlanHandle()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var options = MaxioTestOptions.Valid();
        options.PaymentCollectionMethod = "remittance";

        await Build(client, options).SubscribeAsync(Subscriber, "pro");

        var created = Assert.Single(client.CreatedSubscriptions);
        Assert.Equal("pro", created.ProductHandle);
        Assert.Equal("remittance", created.PaymentCollectionMethod);
        Assert.Null(created.Reference);
    }

    [Fact]
    public async Task SubscribeTwiceReusesTheCustomerAndTheSubscription()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        var first = await service.SubscribeAsync(Subscriber, "pro");
        var second = await service.SubscribeAsync(Subscriber, "pro");

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(client.CreatedSubscriptions);
        Assert.Single(client.CreatedCustomers);
    }

    [Fact]
    public async Task ConcurrentSubscribesEnrolTheShopperExactlyOnce()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) }, CreateDelay = TimeSpan.FromMilliseconds(20) };
        var service = Build(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => service.SubscribeAsync(Subscriber, "pro")));

        Assert.Single(client.CreatedSubscriptions);
        Assert.Single(client.CreatedCustomers);
        Assert.Equal(1, results.Count(result => !result.AlreadySubscribed));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task SubscribeIgnoresATerminatedSubscriptionAndEnrolsAgain()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        var first = await service.SubscribeAsync(Subscriber, "pro");
        client.Subscriptions.Single(subscription => subscription.Id == first.Subscription.Id).State = "canceled";

        var second = await service.SubscribeAsync(Subscriber, "pro");

        Assert.False(second.AlreadySubscribed);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(2, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task SubscribeToADifferentPlanCreatesASecondSubscription()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900), Product("basic", 2900) } };
        var service = Build(client);

        await service.SubscribeAsync(Subscriber, "pro");
        var second = await service.SubscribeAsync(Subscriber, "basic");

        Assert.False(second.AlreadySubscribed);
        Assert.Equal(2, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task SubscribeRejectsAHandleThatIsNotInTheConfiguredFamily()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => Build(client).SubscribeAsync(Subscriber, "some-other-sites-plan"));

        Assert.Empty(client.CreatedSubscriptions);
    }

    [Fact]
    public async Task SubscribeRejectsAnEmptyHandleWithoutCallingTheProvider()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => Build(client).SubscribeAsync(Subscriber, "  "));
    }

    [Fact]
    public async Task SubscribeSurfacesAProviderRejectionAsAValidationFailure()
    {
        var client = new FakeMaxioApiClient
        {
            Products = { Product("pro", 29900) },
            CreateSubscriptionFailure = new MaxioApiException(
                "rejected", HttpStatusCode.UnprocessableEntity, new[] { "No payment method was on file" })
        };

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => Build(client).SubscribeAsync(Subscriber, "pro"));

        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Equal("No payment method was on file", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task SubscribeSurfacesAnUnreachableProviderAsAGatewayFailure()
    {
        var client = new FakeMaxioApiClient
        {
            Products = { Product("pro", 29900) },
            CreateSubscriptionFailure = new MaxioApiException("unreachable")
        };

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => Build(client).SubscribeAsync(Subscriber, "pro"));

        Assert.IsNotType<BillingValidationException>(exception);
    }

    // -- idempotency key ----------------------------------------------------------------------

    [Fact]
    public async Task AnIdempotencyKeyStampsADeterministicSubscriptionReference()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };

        await Build(client).SubscribeAsync(Subscriber, "pro", idempotencyKey: "checkout-1");

        var reference = Assert.Single(client.CreatedSubscriptions).Reference;
        Assert.False(string.IsNullOrWhiteSpace(reference));
        Assert.StartsWith("sub-", reference);
    }

    [Fact]
    public async Task ReplayingAnIdempotencyKeyReturnsTheOriginalSubscription()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        var first = await service.SubscribeAsync(Subscriber, "pro", idempotencyKey: "checkout-1");
        var replay = await service.SubscribeAsync(Subscriber, "pro", idempotencyKey: "checkout-1");

        Assert.True(replay.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, replay.Subscription.Id);
        Assert.Single(client.CreatedSubscriptions);
    }

    [Fact]
    public async Task ARejectedCreateWhoseReferenceNowResolvesReturnsTheWinnersSubscription()
    {
        // Models losing a race to another process: the reference is taken, so the create is
        // rejected, but looking the reference up finds the subscription that won.
        var client = new FakeMaxioApiClient
        {
            Products = { Product("pro", 29900) },
            CreateSubscriptionFailure = new MaxioApiException(
                "reference taken", HttpStatusCode.UnprocessableEntity, new[] { "Reference: must be unique." }),
            SubscriptionForAnyReference = new MaxioSubscription { Id = 4242, State = "active" }
        };

        var result = await Build(client).SubscribeAsync(Subscriber, "pro", idempotencyKey: "checkout-1");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(4242, result.Subscription.Id);
    }

    [Fact]
    public async Task ARejectedCustomerCreateWhoseReferenceNowResolvesReusesThatCustomer()
    {
        var client = new FakeMaxioApiClient
        {
            Products = { Product("pro", 29900) },
            CreateCustomerFailure = new MaxioApiException(
                "reference taken", HttpStatusCode.UnprocessableEntity, new[] { "Reference: must be unique." }),
            CustomerAppearsAfterFailedCreate = new MaxioCustomer { Id = 77, Reference = "eshoponweb:demouser@microsoft.com" }
        };

        var result = await Build(client).SubscribeAsync(Subscriber, "pro");

        Assert.Equal(77, Assert.Single(client.CreatedSubscriptions).CustomerId);
        Assert.False(result.AlreadySubscribed);
    }

    // -- reading back -------------------------------------------------------------------------

    [Fact]
    public async Task GetSubscriptionsIsEmptyForAShopperWhoHasNeverSubscribed()
    {
        Assert.Empty(await Build(new FakeMaxioApiClient()).GetSubscriptionsAsync(Subscriber));
    }

    [Fact]
    public async Task GetSubscriptionsListsLiveSubscriptionsFirst()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900), Product("basic", 2900) } };
        var service = Build(client);

        var cancelled = await service.SubscribeAsync(Subscriber, "pro");
        client.Subscriptions.Single(subscription => subscription.Id == cancelled.Subscription.Id).State = "canceled";
        await service.SubscribeAsync(Subscriber, "basic");

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Equal(2, subscriptions.Count);
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task GetSubscriptionsProjectsThePlanPriceAndNextBillingDate()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        await service.SubscribeAsync(Subscriber, "pro");
        var subscription = Assert.Single(await service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal("pro", subscription.PlanHandle);
        Assert.Equal("299.00", subscription.FormattedPrice);
        Assert.Equal("month", subscription.IntervalUnit);
        Assert.NotNull(subscription.NextBillingAt);
        Assert.Equal("eshoponweb:demouser@microsoft.com", subscription.Customer!.Reference);
    }

    [Fact]
    public async Task SubscribersAreKeptApart()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        await service.SubscribeAsync(new SubscriberIdentity("first@example.com"), "pro");
        await service.SubscribeAsync(new SubscriberIdentity("second@example.com"), "pro");

        Assert.Single(await service.GetSubscriptionsAsync(new SubscriberIdentity("first@example.com")));
        Assert.Single(await service.GetSubscriptionsAsync(new SubscriberIdentity("second@example.com")));
        Assert.Equal(2, client.CreatedCustomers.Count);
    }

    [Fact]
    public async Task TheCustomerReferenceIsCaseInsensitiveSoOneAccountMapsToOneCustomer()
    {
        var client = new FakeMaxioApiClient { Products = { Product("pro", 29900) } };
        var service = Build(client);

        await service.SubscribeAsync(new SubscriberIdentity("DemoUser@Microsoft.com"), "pro");
        var second = await service.SubscribeAsync(new SubscriberIdentity("demouser@microsoft.com"), "pro");

        Assert.True(second.AlreadySubscribed);
        Assert.Single(client.CreatedCustomers);
    }
}
