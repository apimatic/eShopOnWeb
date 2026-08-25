using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private static readonly SubscriberInfo Subscriber = new("user-123", "demouser@microsoft.com");

    private readonly IMaxioBillingClient _maxioClient = Substitute.For<IMaxioBillingClient>();
    private readonly SubscriptionBillingService _service;

    public SubscriptionBillingServiceTests()
    {
        _service = new SubscriptionBillingService(_maxioClient, new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });
    }

    [Fact]
    public async Task ListPlans_ReturnsOnlyNonArchivedProductsSortedByPrice()
    {
        _maxioClient.ListProductsAsync("eshop-subscribe").Returns(new List<MaxioProduct>
        {
            new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
            new() { Id = 3, Handle = "old-plan", Name = "Old Plan", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
        });

        var plans = await _service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle).ToArray());
    }

    [Fact]
    public async Task Subscribe_ReturnsNull_WhenPlanHandleNotOffered()
    {
        _maxioClient.ListProductsAsync("eshop-subscribe").Returns(new List<MaxioProduct>
        {
            new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await _service.SubscribeAsync(Subscriber, "no-such-plan");

        Assert.Null(result);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        SetupPlans();
        _maxioClient.FindCustomerByReferenceAsync(Subscriber.UserId).Returns((MaxioCustomer?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Subscriber.Email, Subscriber.UserId)
            .Returns(new MaxioCustomer { Id = 42, Reference = Subscriber.UserId, Email = Subscriber.Email });
        _maxioClient.ListSubscriptionsByCustomerAsync(42).Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync("eshop-pro", Subscriber.UserId)
            .Returns(ActiveSubscription(id: 9001));

        var result = await _service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.NotNull(result);
        Assert.Equal(9001, result!.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.NotNull(result.NextBillingDate);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingSubscription_WhenAlreadySubscribed()
    {
        SetupPlans();
        _maxioClient.FindCustomerByReferenceAsync(Subscriber.UserId)
            .Returns(new MaxioCustomer { Id = 42, Reference = Subscriber.UserId });
        _maxioClient.ListSubscriptionsByCustomerAsync(42).Returns(new List<MaxioSubscription>
        {
            ActiveSubscription(id: 9001)
        });

        var result = await _service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.NotNull(result);
        Assert.Equal(9001, result!.Id);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Subscribe_CreatesNewSubscription_WhenExistingOneIsCanceled()
    {
        SetupPlans();
        _maxioClient.FindCustomerByReferenceAsync(Subscriber.UserId)
            .Returns(new MaxioCustomer { Id = 42, Reference = Subscriber.UserId });
        _maxioClient.ListSubscriptionsByCustomerAsync(42).Returns(new List<MaxioSubscription>
        {
            ActiveSubscription(id: 9001, state: "canceled")
        });
        _maxioClient.CreateSubscriptionAsync("eshop-pro", Subscriber.UserId)
            .Returns(ActiveSubscription(id: 9002));

        var result = await _service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(9002, result!.Id);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenNoCustomerExists()
    {
        _maxioClient.FindCustomerByReferenceAsync(Subscriber.UserId).Returns((MaxioCustomer?)null);

        var result = await _service.ListSubscriptionsAsync(Subscriber);

        Assert.Empty(result);
    }

    private void SetupPlans()
    {
        _maxioClient.ListProductsAsync("eshop-subscribe").Returns(new List<MaxioProduct>
        {
            new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
    }

    private static MaxioSubscription ActiveSubscription(long id, string state = "active") => new()
    {
        Id = id,
        State = state,
        ProductHandle = "eshop-pro",
        ProductName = "Pro Plan",
        ProductPriceInCents = 29900,
        ProductInterval = 1,
        ProductIntervalUnit = "month",
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
