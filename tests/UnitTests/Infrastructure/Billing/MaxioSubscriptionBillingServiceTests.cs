using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioSubscriptionBillingService _sut;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = FamilyHandle });
        _sut = new MaxioSubscriptionBillingService(_maxio, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task GetAvailablePlans_MapsProductsAndSkipsArchived()
    {
        _maxio.ListProductsForProductFamilyAsync(FamilyHandle, 1, 200, Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new()
                {
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    Description = "Full access",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                },
                new()
                {
                    Handle = "retired-plan",
                    Name = "Retired",
                    PriceInCents = 100,
                    Interval = 1,
                    IntervalUnit = "month",
                    ArchivedAt = "2020-01-01T00:00:00Z"
                },
                new()
                {
                    Handle = "basic-plan",
                    Name = "Basic Plan",
                    PriceInCents = 2900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });

        var plans = await _sut.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
        Assert.Equal("month", plans[1].IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        ArrangeFamilyProducts();
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDto?)null);
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((CustomerDto?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 44, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(44, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionDto>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ActiveProSubscription(901));

        var result = await _sut.SubscribeAsync(DemoSubscriber(), "eshop-pro");

        Assert.Equal(901, result.Subscription.Id);
        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);

        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerRequest>(r => r.Customer.Reference == "user-1" && r.Customer.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(r =>
                r.Subscription.CustomerId == 44 &&
                r.Subscription.ProductHandle == "eshop-pro" &&
                r.Subscription.PaymentCollectionMethod == "remittance" &&
                r.Subscription.Reference == "user-1:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenSubscriptionReferenceAlreadyExists()
    {
        ArrangeFamilyProducts();
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(ActiveProSubscription(77));

        var first = await _sut.SubscribeAsync(DemoSubscriber(), "eshop-pro");
        var second = await _sut.SubscribeAsync(DemoSubscriber(), "eshop-pro");

        Assert.Equal(77, first.Subscription.Id);
        Assert.Equal(77, second.Subscription.Id);
        Assert.False(first.Created);
        Assert.False(second.Created);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesLiveSubscription_WhenCustomerAlreadyEnrolledInPlan()
    {
        ArrangeFamilyProducts();
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDto?)null);
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 44, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(44, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionDto> { ActiveProSubscription(55) });

        var result = await _sut.SubscribeAsync(DemoSubscriber(), "eshop-pro");

        Assert.Equal(55, result.Subscription.Id);
        Assert.False(result.Created);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ThrowsNotFound_WhenHandleIsOutsideConfiguredFamily()
    {
        ArrangeFamilyProducts();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _sut.SubscribeAsync(DemoSubscriber(), "not-a-plan"));
    }

    [Fact]
    public async Task GetSubscriptionsForUser_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((CustomerDto?)null);

        var result = await _sut.GetSubscriptionsForUserAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SplitName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName(DemoSubscriber());

        Assert.Equal("Demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public void ToShopperSubscription_PrefersNextAssessmentAsNextBillingDate()
    {
        var mapped = MaxioSubscriptionBillingService.ToShopperSubscription(ActiveProSubscription(1));

        Assert.Equal(DateTimeOffset.Parse("2026-09-20T12:00:00-04:00"), mapped.NextBillingDate);
        Assert.Equal(299.00m, mapped.Price);
    }

    private void ArrangeFamilyProducts()
    {
        _maxio.ListProductsForProductFamilyAsync(FamilyHandle, 1, 200, Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });
    }

    private static Subscriber DemoSubscriber()
        => new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private static SubscriptionDto ActiveProSubscription(int id)
    {
        return new SubscriptionDto
        {
            Id = id,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = "2026-09-20T12:00:00-04:00",
            CurrentPeriodEndsAt = "2026-09-19T12:00:00-04:00",
            Product = new ProductDto { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
        };
    }
}
