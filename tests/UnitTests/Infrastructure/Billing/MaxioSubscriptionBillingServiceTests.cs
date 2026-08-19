using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
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

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _sut = new MaxioSubscriptionBillingService(_maxio, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListAvailablePlansAsync_MapsMaxioProducts()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new()
            {
                Handle = "eshop-pro",
                Name = "Pro Plan",
                Description = "Pro",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month"
            }
        });

        var plans = await _sut.ListAvailablePlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNeitherExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            ProPlan()
        });
        _maxio.FindCustomerByReferenceAsync("user-1", default).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), default)
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1", Email = "demo@example.com" });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default)
            .Returns(ActiveProSubscription());

        var result = await _sut.SubscribeAsync(SubscribeRequest());

        Assert.True(result.Created);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(c => c.Reference == "user-1" && c.Email == "demo@example.com"),
            default);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(s => s.CustomerId == 42 && s.ProductHandle == "eshop-pro"),
            default);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotCreateDuplicates_WhenLiveSubscriptionExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct> { ProPlan() });
        _maxio.FindCustomerByReferenceAsync("user-1", default)
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>
        {
            ActiveProSubscription()
        });

        var first = await _sut.SubscribeAsync(SubscribeRequest());
        var second = await _sut.SubscribeAsync(SubscribeRequest());

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(9001, first.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesCustomer_WhenCreateReturnsUnprocessableEntity()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct> { ProPlan() });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.When(x => x.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new MaxioApiException("reference already taken", HttpStatusCode.UnprocessableEntity));
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default)
            .Returns(ActiveProSubscription());

        var result = await _sut.SubscribeAsync(SubscribeRequest());

        Assert.True(result.Created);
        await _maxio.Received(2).FindCustomerByReferenceAsync("user-1", default);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsPlanNotFound_ForUnknownHandle()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct> { ProPlan() });

        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            _sut.SubscribeAsync(SubscribeRequest() with { ProductHandle = "not-a-plan" }));
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("user-1", default).Returns((MaxioCustomer?)null);

        var subscriptions = await _sut.ListSubscriptionsForUserAsync("user-1");

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    private static SubscribeToPlan SubscribeRequest() => new()
    {
        UserId = "user-1",
        Email = "demo@example.com",
        UserName = "demo@example.com",
        ProductHandle = "eshop-pro"
    };

    private static MaxioProduct ProPlan() => new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static MaxioSubscription ActiveProSubscription() => new()
    {
        Id = 9001,
        State = "active",
        ProductPriceInCents = 29900,
        NextAssessmentAt = DateTimeOffset.Parse("2026-09-19T00:00:00+00:00"),
        Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
    };
}
