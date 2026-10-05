using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserId = "user-1";
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly MaxioSubscriptionService _service;

    public MaxioSubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });
        _service = new MaxioSubscriptionService(_client, options, new MemoryCache(new MemoryCacheOptions()));
    }

    private static MaxioProduct Product(int id, string handle, long priceInCents, DateTime? archivedAt = null) =>
        new()
        {
            Id = id,
            Handle = handle,
            Name = $"{handle} name",
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Handle = FamilyHandle }
        };

    private static MaxioCustomer Customer(int id = 500) =>
        new() { Id = id, Email = UserId, Reference = UserId };

    private static MaxioSubscription Subscription(int id, string productHandle, string state = "active") =>
        new()
        {
            Id = id,
            State = state,
            ProductPriceInCents = 29900,
            Customer = Customer(),
            Product = new MaxioProduct { Id = 700, Handle = productHandle, Name = productHandle, PriceInCents = 29900 },
            CurrentPeriodEndsAt = DateTime.UtcNow.AddDays(30)
        };

    private void FamilyProducts(params MaxioProduct[] products) =>
        _client.ListProductsForProductFamilyAsync($"handle:{FamilyHandle}", Arg.Any<System.Threading.CancellationToken>())
            .Returns(products.ToList());

    [Fact]
    public async Task SubscribeAsync_WhenUserAlreadySubscribed_ReturnsExistingWithoutCreating()
    {
        FamilyProducts(Product(700, "eshop-pro", 29900));
        _client.FindCustomerByReferenceAsync(UserId).Returns(Customer(500));
        _client.ListCustomerSubscriptionsAsync(500)
            .Returns(new[] { Subscription(900, "eshop-pro") });

        var result = await _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "eshop-pro"));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCustomerCreate>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenNewUserAndPlan_CreatesCustomerAndSubscription()
    {
        FamilyProducts(Product(700, "eshop-pro", 29900));
        _client.FindCustomerByReferenceAsync(UserId).ReturnsNull();
        _client.CreateCustomerAsync(Arg.Any<MaxioCustomerCreate>()).Returns(Customer(501));
        _client.ListCustomerSubscriptionsAsync(501).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>())
            .Returns(Subscription(901, "eshop-pro"));

        var result = await _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(901, result.Subscription.Id);
        Assert.Equal(501, result.CustomerId);
        await _client.Received(1).CreateCustomerAsync(Arg.Is<MaxioCustomerCreate>(c =>
            c.Reference == UserId && c.Email == "a@b.c"));
        await _client.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioSubscriptionCreate>(s =>
            s.ProductHandle == "eshop-pro" &&
            s.CustomerId == 501 &&
            s.Reference == $"{UserId}:eshop-pro" &&
            s.PaymentCollectionMethod == "remittance"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenUnknownPlan_ThrowsPlanNotFound()
    {
        FamilyProducts(Product(700, "eshop-pro", 29900));
        _client.FindCustomerByReferenceAsync(UserId).Returns(Customer(500));
        _client.ListCustomerSubscriptionsAsync(500).Returns(Array.Empty<MaxioSubscription>());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "no-such-plan")));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenArchivedPlan_ThrowsPlanNotFound()
    {
        FamilyProducts(Product(700, "eshop-pro", 29900, archivedAt: DateTime.UtcNow));
        _client.FindCustomerByReferenceAsync(UserId).Returns(Customer(500));
        _client.ListCustomerSubscriptionsAsync(500).Returns(Array.Empty<MaxioSubscription>());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "eshop-pro")));
    }

    [Fact]
    public async Task SubscribeAsync_TwiceForSameUserAndPlan_ReturnsSameSubscription()
    {
        FamilyProducts(Product(700, "eshop-pro", 29900));
        _client.FindCustomerByReferenceAsync(UserId).Returns(Customer(500));
        _client.ListCustomerSubscriptionsAsync(500).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>())
            .Returns(Subscription(901, "eshop-pro"));

        var first = await _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "eshop-pro"));
        _client.ListCustomerSubscriptionsAsync(500).Returns(new[] { Subscription(901, "eshop-pro") });
        var second = await _service.SubscribeAsync(new SubscribeCommand(UserId, "a@b.c", "a", "b", "eshop-pro"));

        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.True(second.AlreadySubscribed);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_WhenNoMaxioCustomer_ReturnsEmptyList()
    {
        _client.FindCustomerByReferenceAsync(UserId).ReturnsNull();

        var result = await _service.ListMySubscriptionsAsync(UserId);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_WhenCustomerExists_ReturnsSubscriptions()
    {
        _client.FindCustomerByReferenceAsync(UserId).Returns(Customer(500));
        _client.ListCustomerSubscriptionsAsync(500)
            .Returns(new[] { Subscription(901, "eshop-pro"), Subscription(902, "basic-plan", "canceled") });

        var result = await _service.ListMySubscriptionsAsync(UserId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Id == 901 && s.State == "active");
        Assert.Contains(result, s => s.Id == 902 && s.State == "canceled");
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedProductsAndMapsPrice()
    {
        FamilyProducts(
            Product(700, "eshop-pro", 29900),
            Product(701, "basic-plan", 2900),
            Product(702, "old-plan", 100, archivedAt: DateTime.UtcNow));

        var plans = await _service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal(299m, plans[1].Price);
        Assert.All(plans, p => Assert.Equal(FamilyHandle, p.ProductFamilyHandle));
    }
}