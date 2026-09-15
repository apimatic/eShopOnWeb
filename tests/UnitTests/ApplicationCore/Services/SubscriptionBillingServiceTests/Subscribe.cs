using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly Shopper _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private readonly BillingProduct _proPlan = new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _gateway.ListProductsForFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(_shopper, _shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _gateway.CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900,
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow
            });

        var service = new SubscriptionBillingService(_gateway, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(299.00m, result.Price);
        await _gateway.Received(1).CreateCustomerAsync(_shopper, _shopper.UserId, Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionInsteadOfCreatingAnother()
    {
        _gateway.ListProductsForFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _gateway.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription
            {
                Id = 7,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var service = new SubscriptionBillingService(_gateway, _logger);
        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(7, first.Id);
        Assert.Equal(7, second.Id);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<Shopper>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenPlanHandleIsUnknown()
    {
        _gateway.ListProductsForFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        var service = new SubscriptionBillingService(_gateway, _logger);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(_shopper, "does-not-exist"));
    }

    [Fact]
    public async Task ListPlansSkipsArchivedProducts()
    {
        _gateway.ListProductsForFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            _proPlan,
            new BillingProduct
            {
                Id = 2,
                Handle = "old-plan",
                Name = "Old",
                PriceInCents = 100,
                Interval = 1,
                IntervalUnit = "month",
                ArchivedAt = DateTimeOffset.UtcNow.ToString("O")
            }
        });

        var service = new SubscriptionBillingService(_gateway, _logger);
        var plans = await service.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
    }

    [Fact]
    public async Task GetMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var service = new SubscriptionBillingService(_gateway, _logger);
        var subscriptions = await service.GetMySubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
    }
}
