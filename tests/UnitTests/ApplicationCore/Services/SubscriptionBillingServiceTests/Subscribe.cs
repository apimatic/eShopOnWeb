using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private const string UserId = "user-guid-1";
    private const string Email = "demouser@microsoft.com";
    private readonly IMaxioClient _maxio = Substitute.For<IMaxioClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();

    public Subscribe()
    {
        _maxio.ListProductsForConfiguredFamilyAsync(default).Returns(new List<SubscriptionPlan>
        {
            new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", Price = 299m, Interval = 1, IntervalUnit = "month" },
            new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", Price = 29m, Interval = 1, IntervalUnit = "month" }
        });
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWhenReferenceAlreadyExists()
    {
        var existing = new ShopperSubscription
        {
            Id = 42,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            Price = 299m,
            State = "active",
            NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
        };
        _maxio.GetSubscriptionByReferenceAsync(SubscriptionBillingService.BuildSubscriptionReference(UserId, "eshop-pro"), default)
            .Returns(existing);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(UserId, Email, Email, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(42, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.GetSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.GetCustomerByReferenceAsync(UserId, default).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new BillingCustomer { Id = 9, Reference = UserId, Email = Email });
        _maxio.ListCustomerSubscriptionsAsync(9, default).Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(9, "eshop-pro", Arg.Any<string>(), default)
            .Returns(new ShopperSubscription
            {
                Id = 100,
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                Price = 299m,
                State = "active"
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(UserId, Email, Email, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(100, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), default);
        await _maxio.Received(1).CreateSubscriptionAsync(9, "eshop-pro", $"{UserId}:eshop-pro", default);
    }

    [Fact]
    public async Task ReusesCustomerCreatedByAConcurrentRequest()
    {
        _maxio.GetSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.GetCustomerByReferenceAsync(UserId, default)
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 11, Reference = UserId, Email = Email });
        _maxio.CreateCustomerAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), default)
            .ThrowsAsync(new MaxioApiException((int)HttpStatusCode.UnprocessableEntity, "reference taken"));
        _maxio.ListCustomerSubscriptionsAsync(11, default).Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(11, "basic-plan", Arg.Any<string>(), default)
            .Returns(new ShopperSubscription { Id = 7, ProductHandle = "basic-plan", State = "active" });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(UserId, Email, Email, "basic-plan");

        Assert.True(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await _maxio.Received(2).GetCustomerByReferenceAsync(UserId, default);
    }

    [Fact]
    public async Task ThrowsWhenProductHandleIsNotInTheConfiguredFamily()
    {
        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(UserId, Email, Email, "not-a-plan"));
    }

    [Fact]
    public void SplitDisplayNameUsesEmailLocalPart()
    {
        var (first, last) = SubscriptionBillingService.SplitDisplayName(null, "demouser@microsoft.com");
        Assert.Equal("Shopper", first);
        Assert.Equal("demouser", last);
    }
}

public class ListSubscriptionsForUser
{
    [Fact]
    public async Task ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        maxio.GetCustomerByReferenceAsync("missing", default).Returns((BillingCustomer?)null);

        var service = new SubscriptionBillingService(maxio, logger);
        var result = await service.ListSubscriptionsForUserAsync("missing");

        Assert.Empty(result);
    }
}
