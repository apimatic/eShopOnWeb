using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscriptionBillingService _sut;
    private readonly ShopperIdentity _shopper = new("user-1", "shopper@contoso.com", "Demo", "Shopper");
    private readonly SubscriptionPlan _basic = new()
    {
        ProductId = 42,
        Handle = "basic",
        Name = "Basic",
        Price = 9.99m,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    public Subscribe()
    {
        _sut = new SubscriptionBillingService(_maxio, _logger);
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _basic });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = _shopper.UserId, Email = _shopper.Email };
        var created = NewSubscription(100, "active");

        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null, customer);
        _maxio.CreateCustomerAsync(_shopper, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>(), new[] { created });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(7, "basic", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await _sut.SubscribeAsync(_shopper, "basic");

        Assert.Equal(100, result.SubscriptionId);
        Assert.Equal("active", result.State);
        await _maxio.Received(1).CreateCustomerAsync(_shopper, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(7, "basic", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateSecondSubscriptionWhenLiveSubscriptionExists()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = _shopper.UserId };
        var existing = NewSubscription(100, "active");

        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new[] { existing });

        var first = await _sut.SubscribeAsync(_shopper, "basic");
        var second = await _sut.SubscribeAsync(_shopper, "BASIC");

        Assert.Equal(first.SubscriptionId, second.SubscriptionId);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWhenCreateConflicts()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = _shopper.UserId };
        var existing = NewSubscription(100, "active");

        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>(), new[] { existing });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null, existing);
        _maxio.CreateSubscriptionAsync(7, "basic", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ShopperSubscription>(_ => throw new MaxioApiException(422, "reference already taken"));

        var result = await _sut.SubscribeAsync(_shopper, "basic");

        Assert.Equal(100, result.SubscriptionId);
    }

    [Fact]
    public async Task ThrowsWhenProductHandleIsUnknown()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SubscribeAsync(_shopper, "does-not-exist"));
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerMissing()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _sut.ListMySubscriptionsAsync(_shopper.UserId);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BuildsStableSubscriptionReference()
    {
        var reference = SubscriptionBillingService.BuildSubscriptionReference("user-1", "basic");
        Assert.Equal("eshop:user-1:basic", reference);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    public void ClassifiesLiveStates(string state, bool expectedLive)
    {
        Assert.Equal(expectedLive, SubscriptionBillingService.IsLive(state));
    }

    private static ShopperSubscription NewSubscription(int id, string state) => new()
    {
        SubscriptionId = id,
        State = state,
        CustomerId = 7,
        ProductHandle = "basic",
        ProductName = "Basic",
        Price = 9.99m
    };
}
