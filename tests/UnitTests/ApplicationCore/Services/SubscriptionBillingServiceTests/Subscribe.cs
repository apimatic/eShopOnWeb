using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        Description = "Pro",
        Price = 299m,
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(_shopper, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<CustomerSubscription>());
        var created = LiveSubscription(99, "eshop-pro");
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(_shopper, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingCustomerAndDoesNotCreateASecondSubscription()
    {
        var customer = new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email };
        var existing = LiveSubscription(99, "eshop-pro");

        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<CustomerSubscription> { existing });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(99, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateCustomerConflicts()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = _shopper.UserId, Email = _shopper.Email };
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, customer);
        _maxio.CreateCustomerAsync(_shopper, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<MaxioCustomer>>(_ => throw new MaxioBillingException("taken", 422));
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LiveSubscription(11, "eshop-pro"));

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(11, result.Subscription.Id);
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionConflicts()
    {
        var customer = new MaxioCustomer { Id = 42, Reference = _shopper.UserId };
        var recovered = LiveSubscription(55, "eshop-pro");

        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>(), new List<CustomerSubscription> { recovered });
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<CustomerSubscription>>(_ => throw new MaxioBillingException("duplicate", 409));

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(55, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsUnknownPlanHandle()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubscribeAsync(_shopper, "not-a-plan"));
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsEmptyListWhenShopperHasNoMaxioCustomer()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        var service = new SubscriptionBillingService(_maxio, _logger);

        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DisplayNameSplitsEmailLocalPart()
    {
        var names = _shopper.DisplayName();
        Assert.Equal("Demouser", names.FirstName);
        Assert.Equal("Shopper", names.LastName);
    }

    private static CustomerSubscription LiveSubscription(int id, string handle) => new()
    {
        Id = id,
        State = "active",
        ProductHandle = handle,
        ProductName = "Pro Plan",
        Price = 299m,
        PriceInCents = 29900,
        NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
    };
}
