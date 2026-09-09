using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const long FamilyId = 100;

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();

    private MaxioSubscriptionService CreateService()
    {
        _client.GetProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductFamily> { new() { Id = FamilyId, Handle = FamilyHandle } });

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "sub",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioSubscriptionService(_client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private void SetupProducts(params MaxioProduct[] products)
        => _client.GetProductsInFamilyAsync(FamilyId, Arg.Any<CancellationToken>())
                  .Returns(new List<MaxioProduct>(products));

    private static MaxioProduct Product(string handle, string name, int cents, System.DateTimeOffset? archivedAt = null)
        => new() { Handle = handle, Name = name, PriceInCents = cents, Interval = 1, IntervalUnit = "month", ArchivedAt = archivedAt };

    private static SubscriberInfo Subscriber() => SubscriberInfo.FromEmail("demouser@microsoft.com");

    [Fact]
    public async Task GetPlansAsync_FiltersArchived_AndOrdersByPrice()
    {
        var service = CreateService();
        SetupProducts(
            Product("eshop-pro", "Pro Plan", 29900),
            Product("basic-plan", "Basic Plan", 2900),
            Product("legacy", "Legacy", 100, archivedAt: System.DateTimeOffset.UtcNow));

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal("$29.00/month", plans[0].DisplayPrice);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExists()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CustomerAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "demouser@microsoft.com" });
        _client.GetCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 999,
                State = "active",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });

        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal(42, result.CustomerId);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<CustomerAttributes>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingLiveSubscription_AndDoesNotCreate()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.GetCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 555,
                    State = "active",
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
                }
            });

        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CustomerAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNew_WhenExistingSubscriptionIsCanceled()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.GetCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 555, State = "canceled", Product = new MaxioProduct { Handle = "eshop-pro" } }
            });
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 777, State = "active", Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" } });

        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_Throws400_WhenPlanUnknown()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber(), "nope"));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_Throws400_WhenPlanHandleMissing()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber(), ""));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_RecoversFromDuplicateReferenceRace()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));

        // First lookup returns null (no customer), create loses the race (422 duplicate),
        // second lookup finds the concurrently-created customer.
        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 88 });
        _client.CreateCustomerAsync(Arg.Any<CustomerAttributes>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(
                System.Net.HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." },
                null));
        _client.GetCustomerSubscriptionsAsync(88, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 1, State = "active", Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" } });

        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro");

        Assert.Equal(88, result.CustomerId);
        Assert.False(result.AlreadyExisted);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subs = await service.GetSubscriptionsAsync(Subscriber());

        Assert.Empty(subs);
        await _client.DidNotReceive().GetCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WrapsUpstreamFailure_As502()
    {
        var service = CreateService();
        SetupProducts(Product("eshop-pro", "Pro Plan", 29900));
        _client.LookupCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.GetCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscription>(_ => throw new MaxioApiException(
                System.Net.HttpStatusCode.InternalServerError, System.Array.Empty<string>(), "boom"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber(), "eshop-pro"));

        Assert.Equal(502, ex.StatusCode);
    }
}
