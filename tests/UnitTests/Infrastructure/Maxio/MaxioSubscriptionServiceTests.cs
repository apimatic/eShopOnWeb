using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Behavioural tests for <see cref="MaxioSubscriptionService"/> against a faked Maxio API client,
/// covering plan filtering, the subscribe hero flow, and idempotency.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com", null, null);

    private MaxioSubscriptionService CreateService()
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,
        });

        // Every plan lookup resolves a site currency; default it unless a test overrides.
        _client.GetSiteCurrencyAsync(Arg.Any<CancellationToken>()).Returns("USD");

        return new MaxioSubscriptionService(
            _client,
            settings,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private void SeedCatalog() =>
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProductDto>
        {
            Product("eshop-pro", "Pro Plan", 29900, FamilyHandle),
            Product("basic-plan", "Basic Plan", 2900, FamilyHandle),
            Product("other-plan", "Other", 100, "different-family"),      // wrong family -> excluded
            Product("legacy", "Legacy", 500, FamilyHandle, archived: true), // archived -> excluded
        });

    [Fact]
    public async Task GetAvailablePlans_ReturnsOnlyOfferedFamily_SortedByPrice_WithCurrency()
    {
        SeedCatalog();
        var service = CreateService();

        var plans = await service.GetAvailablePlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle).ToArray());
        Assert.Equal(2900, plans[0].PriceInCents);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("USD", plans[0].Currency);
        Assert.Equal("month", plans[0].IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Throws()
    {
        SeedCatalog();
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(_subscriber, "ghost-plan"));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_NewCustomer_CreatesCustomerThenSubscription_ViaRemittance()
    {
        SeedCatalog();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerDto?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.Reference));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1000, "active", "eshop-pro", 29900));

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1000, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerDto>(c => c.Reference == _subscriber.Reference && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionDto>(s => s.ProductHandle == "eshop-pro" && s.CustomerId == 42 && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ExistingActiveSubscription_IsIdempotent_NoCreate()
    {
        SeedCatalog();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.Reference));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto> { Subscription(1000, "active", "eshop-pro", 29900) });

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(1000, result.Subscription.Id);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CanceledSubscriptionForPlan_CreatesNew()
    {
        SeedCatalog();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.Reference));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto> { Subscription(900, "canceled", "eshop-pro", 29900) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1001, "active", "eshop-pro", 29900));

        var service = CreateService();
        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1001, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_NoCustomer_ReturnsEmpty()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerDto?)null);

        var service = CreateService();
        var subs = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subs);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_MapsAndOrdersByCreatedDescending()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.Reference));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto>
            {
                Subscription(1, "active", "basic-plan", 2900, createdAt: "2026-01-01T00:00:00-05:00"),
                Subscription(2, "active", "eshop-pro", 29900, createdAt: "2026-03-01T00:00:00-05:00"),
            });

        var service = CreateService();
        var subs = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Equal(new[] { 2, 1 }, subs.Select(s => s.Id).ToArray());
        Assert.Equal("eshop-pro", subs[0].PlanHandle);
        Assert.Equal(299.00m, subs[0].Price);
    }

    private static MaxioProductDto Product(string handle, string name, long priceCents, string family, bool archived = false) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = name,
        PriceInCents = priceCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archived ? "2020-01-01T00:00:00-05:00" : null,
        ProductFamily = new MaxioProductFamilyDto { Handle = family, Name = family },
    };

    private static MaxioCustomerDto Customer(int id, string reference) => new()
    {
        Id = id,
        Reference = reference,
        Email = reference,
    };

    private static MaxioSubscriptionDto Subscription(int id, string state, string productHandle, long priceCents, string? createdAt = null) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = priceCents,
        Currency = "USD",
        CurrentPeriodEndsAt = "2026-09-01T00:00:00-05:00",
        CreatedAt = createdAt ?? "2026-07-01T00:00:00-05:00",
        Product = new MaxioProductDto { Handle = productHandle, Name = productHandle },
    };
}
