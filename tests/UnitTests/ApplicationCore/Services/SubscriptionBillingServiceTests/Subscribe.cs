using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private const string UserId = "user-123";
    private const string Email = "demouser@microsoft.com";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();

    public Subscribe()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan>
        {
            new() { Handle = "basic-plan", Name = "Basic Plan", Price = 29m, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "eshop-pro", Name = "Pro Plan", Price = 299m, Interval = 1, IntervalUnit = "month" }
        });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(UserId, Arg.Any<string>(), Arg.Any<string>(), Email, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = UserId, Email = Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription
            {
                Id = 99,
                CustomerId = 42,
                PlanHandle = "eshop-pro",
                PlanName = "Pro Plan",
                Price = 299m,
                State = "active",
                NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        await _maxio.Received(1).CreateCustomerAsync(UserId, Arg.Any<string>(), Arg.Any<string>(), Email, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateSecondCustomerWhenReferenceAlreadyExists()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = UserId, Email = Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 99, PlanHandle = "eshop-pro", State = "active" });

        var service = new SubscriptionBillingService(_maxio, _logger);
        await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "eshop-pro"
        });

        await _maxio.DidNotReceive().CreateCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionInsteadOfCreatingAnother()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = UserId, Email = Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>
            {
                new()
                {
                    Id = 77,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    Price = 299m,
                    State = "active",
                    NextBillingAt = DateTimeOffset.UtcNow.AddDays(20)
                }
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversFromDuplicateCustomerCreateByLookingUpReference()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(
                (BillingCustomer?)null,
                new BillingCustomer { Id = 42, Reference = UserId, Email = Email });
        _maxio.When(x => x.CreateCustomerAsync(UserId, Arg.Any<string>(), Arg.Any<string>(), Email, Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Throw(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference must be unique."));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "basic-plan", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 5, PlanHandle = "basic-plan", State = "active" });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "basic-plan"
        });

        Assert.True(result.Created);
        Assert.Equal(5, result.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsWhenRequestedPlanIsNotInTheFamily()
    {
        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(new SubscribeToPlanRequest
            {
                UserId = UserId,
                Email = Email,
                UserName = Email,
                ProductHandle = "does-not-exist"
            }));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        var service = new SubscriptionBillingService(_maxio, _logger);

        var subscriptions = await service.ListSubscriptionsForUserAsync(UserId);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
