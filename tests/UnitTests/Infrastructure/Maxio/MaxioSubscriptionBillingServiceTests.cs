using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "test-family";
    private const long FamilyId = 12345;

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly SubscriberIdentity _subscriber = new("user@example.com", "user@example.com", "user", "eShopOnWeb");

    private MaxioSubscriptionBillingService CreateService()
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-sub",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioSubscriptionBillingService(_client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private void SetupFamily() =>
        _client.ListProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductFamily> { new() { Id = FamilyId, Handle = FamilyHandle, Name = "eShopSubscribe" } });

    private void SetupProducts(params MaxioProduct[] products) =>
        _client.ListProductsForFamilyAsync(FamilyId, Arg.Any<CancellationToken>())
            .Returns(products.ToList());

    private static MaxioProduct Product(string handle, int cents, DateTimeOffset? archivedAt = null) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = handle,
        PriceInCents = cents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt,
    };

    [Fact]
    public async Task ListPlans_FiltersArchived_AndOrdersByPrice()
    {
        SetupFamily();
        SetupProducts(
            Product("pro", 29900),
            Product("basic", 2900),
            Product("legacy", 100, archivedAt: DateTimeOffset.UtcNow));

        var service = CreateService();

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic", plans[0].Handle); // cheapest first
        Assert.Equal("pro", plans[1].Handle);
        Assert.DoesNotContain(plans, p => p.Handle == "legacy");
    }

    [Fact]
    public async Task ListPlans_Throws_WhenConfiguredFamilyMissing()
    {
        _client.ListProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductFamily> { new() { Id = 1, Handle = "other" } });

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.ListPlansAsync());
    }

    [Fact]
    public async Task Subscribe_Throws_PlanNotFound_ForUnknownHandle()
    {
        SetupFamily();
        SetupProducts(Product("pro", 29900));

        var service = CreateService();

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(_subscriber, "ghost"));
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        SetupFamily();
        SetupProducts(Product("pro", 29900));
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = Product("pro", 29900) });

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, "pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerBody>(b => b.Reference == _subscriber.Reference), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(b => b.ProductHandle == "pro" && b.CustomerId == 42 && b.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WhenAlreadyActivelySubscribed()
    {
        SetupFamily();
        SetupProducts(Product("pro", 29900));
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 555, State = "active", Product = Product("pro", 29900) },
            });

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, "pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesNew_WhenExistingSubscriptionIsCanceled()
    {
        SetupFamily();
        SetupProducts(Product("pro", 29900));
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7 });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 100, State = "canceled", Product = Product("pro", 29900) },
            });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 200, State = "active", Product = Product("pro", 29900) });

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, "pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(200, result.Subscription.Id);
    }

    [Fact]
    public async Task EnsureCustomer_RecoversFromRace_WhenCreateReturns422()
    {
        SetupFamily();
        SetupProducts(Product("pro", 29900));
        // First lookup: not found. Create loses a race (422). Second lookup: found.
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 71 });
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(
                HttpStatusCode.UnprocessableEntity, "POST", "customers.json", new[] { "Reference: has already been taken." }));
        _client.ListCustomerSubscriptionsAsync(71, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 900, State = "active", Product = Product("pro", 29900) });

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, "pro");

        Assert.False(result.AlreadyExisted);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(b => b.CustomerId == 71), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();

        var result = await service.ListSubscriptionsAsync(_subscriber);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
