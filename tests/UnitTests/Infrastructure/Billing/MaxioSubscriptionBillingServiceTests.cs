using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioSubscriptionBillingService _sut;
    private readonly Shopper _shopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _sut = new MaxioSubscriptionBillingService(_maxio, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListAvailablePlansAsync_OmitsArchivedProducts_AndMapsPriceFromCents()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-basic", Name = "Basic", Description = "Basic plan", PriceInCents = 900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "eshop-pro", Name = "Pro", Description = "Pro plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "old", Name = "Old", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await _sut.ListAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-basic", plans[0].Handle);
        Assert.Equal(9.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsPlanNotFound_WhenHandleIsNotInFamily()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });

        await Assert.ThrowsAsync<PlanNotFoundException>(() => _sut.SubscribeAsync(_shopper, "no-such-plan"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenReferenceAlreadyExists()
    {
        SeedSinglePlan();
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-123:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 2900,
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro", PriceInCents = 2900 }
            });

        var result = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(29.00m, result.Price);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerOnce_ThenCreatesSubscription()
    {
        SeedSinglePlan();
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription)null);
        _maxio.FindCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Email = _shopper.Email, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 501,
                State = "active",
                ProductPriceInCents = 2900,
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro" }
            });

        var result = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(501, result.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == "user-123" && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s =>
                s.ProductHandle == "eshop-pro" &&
                s.CustomerId == 7 &&
                s.PaymentCollectionMethod == "remittance" &&
                s.Reference == "eshop:user-123:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomer_AndSkipsCreateWhenLivePlanExists()
    {
        SeedSinglePlan();
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription)null);
        _maxio.FindCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 44,
                    State = "active",
                    ProductPriceInCents = 2900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro" }
                }
            });

        var result = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(44, result.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer)null);

        var result = await _sut.ListSubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void SeedSinglePlan()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro", Description = "Pro", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });
    }
}
