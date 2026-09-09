using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly BillingUser _user = BillingUser.FromUserName("shopper@example.com");

    private MaxioBillingService CreateService(string familyHandle = "sample-family")
    {
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = familyHandle,
        });
        return new MaxioBillingService(_client, options, new KeyedAsyncLock(),
            Substitute.For<ILogger<MaxioBillingService>>());
    }

    private static MaxioCustomer Customer(int id = 42) => new() { Id = id, Reference = "shopper@example.com" };

    private static SubscriptionEnvelope SubscriptionEnvelope(int id, string state, string planHandle) => new()
    {
        Subscription = new MaxioSubscription
        {
            Id = id,
            State = state,
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = planHandle, Name = "Pro Plan" },
        },
    };

    [Fact]
    public async Task Subscribe_ReturnsExistingWithoutCreating_WhenLiveSubscriptionForPlanExists()
    {
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionEnvelope> { SubscriptionEnvelope(100, "active", "eshop-pro") });

        var result = await CreateService().SubscribeAsync(_user, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(100, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesNew_WhenNoSubscriptionExists()
    {
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionEnvelope>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 200, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await CreateService().SubscribeAsync(_user, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(200, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesNew_WhenOnlyCanceledSubscriptionForPlanExists()
    {
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionEnvelope> { SubscriptionEnvelope(100, "canceled", "eshop-pro") });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 201, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await CreateService().SubscribeAsync(_user, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesCustomer_WhenNoneExists()
    {
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionEnvelope>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 202, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await CreateService().SubscribeAsync(_user, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_RecoversFromDuplicateCustomerRace()
    {
        // First lookup: not found -> attempt create -> 422 (someone else won the race) -> re-read succeeds.
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer());
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingException("duplicate", upstreamStatusCode: 422));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionEnvelope>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 203, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await CreateService().SubscribeAsync(_user, "eshop-pro");

        Assert.Equal(203, result.Subscription.Id);
    }

    [Fact]
    public async Task GetSubscriptionsForUser_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _client.LookupCustomerByReferenceAsync(_user.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateService().GetSubscriptionsForUserAsync(_user);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPlans_ThrowsWhenFamilyHandleMissing()
    {
        var service = CreateService(familyHandle: "");

        await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());
    }

    [Fact]
    public async Task GetPlans_FiltersArchivedAndMaps()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProductEnvelope>
            {
                new() { Product = new MaxioProduct { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" } },
                new() { Product = new MaxioProduct { Id = 2, Handle = "old", Name = "Archived", ArchivedAt = System.DateTimeOffset.UtcNow } },
            });

        var plans = await CreateService().GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
    }
}
