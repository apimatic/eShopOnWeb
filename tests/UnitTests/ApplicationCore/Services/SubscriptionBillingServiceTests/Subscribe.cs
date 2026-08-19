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
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private SubscriptionBillingService CreateService() => new(_maxio, _options, _logger);

    private static BillingPlan ProPlan() => new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Price = 299.00m,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static BillingPlan BasicPlan() => new()
    {
        Id = 2,
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceInCents = 2900,
        Price = 29.00m,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static SubscribeToPlanRequest SubscribeRequest(string handle = "eshop-pro") => new()
    {
        CustomerReference = "user-123",
        Email = "demouser@microsoft.com",
        FirstName = "Demouser",
        LastName = "Customer",
        ProductHandle = handle
    };

    [Fact]
    public async Task ListPlans_ReturnsFamilyProductsOrderedByPrice()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan(), BasicPlan() });

        var result = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, result.Select(p => p.Handle).ToArray());
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_WhenShopperIsNew()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });
        _maxio.ReadCustomerByReferenceAsync("user-123", default).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync("Demouser", "Customer", "demouser@microsoft.com", "user-123", default)
            .Returns(new BillingCustomer { Id = 42, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<BillingSubscription>());
        _maxio.FindSubscriptionByReferenceAsync("user-123:eshop-pro", default).Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", "user-123:eshop-pro", "remittance", default)
            .Returns(new BillingSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900,
                Price = 299.00m
            });

        var result = await CreateService().SubscribeAsync(SubscribeRequest());

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync("Demouser", "Customer", "demouser@microsoft.com", "user-123", default);
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "user-123:eshop-pro", "remittance", default);
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomer_WhenReferenceAlreadyExists()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });
        _maxio.ReadCustomerByReferenceAsync("user-123", default)
            .Returns(new BillingCustomer { Id = 7, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(7, default).Returns(new List<BillingSubscription>());
        _maxio.FindSubscriptionByReferenceAsync("user-123:eshop-pro", default).Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync(7, "eshop-pro", "user-123:eshop-pro", "remittance", default)
            .Returns(new BillingSubscription { Id = 11, State = "active", ProductHandle = "eshop-pro" });

        await CreateService().SubscribeAsync(SubscribeRequest());

        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionAlreadyExists()
    {
        var existing = new BillingSubscription
        {
            Id = 55,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900,
            Price = 299.00m
        };

        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });
        _maxio.ReadCustomerByReferenceAsync("user-123", default)
            .Returns(new BillingCustomer { Id = 7, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(7, default).Returns(new List<BillingSubscription> { existing });

        var first = await CreateService().SubscribeAsync(SubscribeRequest());
        var second = await CreateService().SubscribeAsync(SubscribeRequest());

        Assert.Equal(55, first.Id);
        Assert.Equal(55, second.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task Subscribe_CreatesNewSubscription_WhenPreviousOneIsCanceled()
    {
        var canceled = new BillingSubscription
        {
            Id = 55,
            State = "canceled",
            ProductHandle = "eshop-pro"
        };
        var created = new BillingSubscription
        {
            Id = 88,
            State = "active",
            ProductHandle = "eshop-pro"
        };

        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });
        _maxio.ReadCustomerByReferenceAsync("user-123", default)
            .Returns(new BillingCustomer { Id = 7, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(7, default).Returns(new List<BillingSubscription> { canceled });
        _maxio.FindSubscriptionByReferenceAsync("user-123:eshop-pro", default).Returns(canceled);
        _maxio.CreateSubscriptionAsync(7, "eshop-pro", Arg.Is<string>(r => r.StartsWith("user-123:eshop-pro:")), "remittance", default)
            .Returns(created);

        var result = await CreateService().SubscribeAsync(SubscribeRequest());

        Assert.Equal(88, result.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(7, "eshop-pro", Arg.Is<string>(r => r.StartsWith("user-123:eshop-pro:")), "remittance", default);
    }

    [Fact]
    public async Task Subscribe_RecoversExistingCustomer_WhenCreateReturnsUnprocessable()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });
        _maxio.ReadCustomerByReferenceAsync("user-123", default)
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 7, Reference = "user-123" });
        _maxio.CreateCustomerAsync("Demouser", "Customer", "demouser@microsoft.com", "user-123", default)
            .Returns<BillingCustomer>(_ => throw new MaxioClientException(422, "Reference has already been taken"));
        _maxio.ListCustomerSubscriptionsAsync(7, default).Returns(new List<BillingSubscription>());
        _maxio.FindSubscriptionByReferenceAsync("user-123:eshop-pro", default).Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync(7, "eshop-pro", "user-123:eshop-pro", "remittance", default)
            .Returns(new BillingSubscription { Id = 11, State = "active", ProductHandle = "eshop-pro" });

        var result = await CreateService().SubscribeAsync(SubscribeRequest());

        Assert.Equal(11, result.Id);
    }

    [Fact]
    public async Task Subscribe_Throws_WhenProductHandleIsNotInFamily()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default)
            .Returns(new List<BillingPlan> { ProPlan() });

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => CreateService().SubscribeAsync(SubscribeRequest("not-a-plan")));
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("user-123", default).Returns((BillingCustomer?)null);

        var result = await CreateService().ListMySubscriptionsAsync("user-123");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsCustomerSubscriptions()
    {
        _maxio.ReadCustomerByReferenceAsync("user-123", default)
            .Returns(new BillingCustomer { Id = 7, Reference = "user-123" });
        _maxio.ListCustomerSubscriptionsAsync(7, default)
            .Returns(new List<BillingSubscription>
            {
                new() { Id = 1, State = "active", ProductHandle = "eshop-pro" }
            });

        var result = await CreateService().ListMySubscriptionsAsync("user-123");

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }
}
