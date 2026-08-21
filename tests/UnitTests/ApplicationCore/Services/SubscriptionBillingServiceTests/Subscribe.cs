using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private SubscriptionBillingService CreateSut()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example-site",
            ProductFamilyHandle = FamilyHandle
        });
        return new SubscriptionBillingService(_maxio, options, NullLogger<SubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task CreatesCustomerThenSubscriptionWhenShopperIsNew()
    {
        _maxio.ReadProductByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(ProProduct());
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ActiveProSubscription(99, 42));

        var result = await CreateSut().SubscribeAsync(_shopper, ProHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(ProHandle, result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);

        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c =>
                c.Reference == _shopper.UserId &&
                c.Email == _shopper.Email &&
                c.FirstName == "demouser" &&
                c.LastName == "eShopOnWeb"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s =>
                s.ProductHandle == ProHandle &&
                s.CustomerId == 42 &&
                s.Reference == "user-1:eshop-pro" &&
                s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _maxio.ReadProductByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(ProProduct());
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { ActiveProSubscription(99, 42) });

        var first = await CreateSut().SubscribeAsync(_shopper, ProHandle);
        var second = await CreateSut().SubscribeAsync(_shopper, ProHandle);

        Assert.True(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(99, first.Subscription.Id);
        Assert.Equal(99, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingCustomerWhenCreateCollidesOnReference()
    {
        _maxio.ReadProductByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(ProProduct());
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns<Task<MaxioCustomer>>(_ => throw new BillingGatewayException("reference taken", 422));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ActiveProSubscription(7, 42));

        var result = await CreateSut().SubscribeAsync(_shopper, ProHandle);

        Assert.Equal(7, result.Subscription.Id);
        await _maxio.Received(2).ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsPlanNotFoundWhenHandleIsUnknown()
    {
        _maxio.ReadProductByHandleAsync("missing-plan", Arg.Any<CancellationToken>()).Returns((MaxioProduct?)null);

        await Assert.ThrowsAsync<PlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, "missing-plan"));
    }

    [Fact]
    public async Task ThrowsPlanNotFoundWhenProductIsOutsideConfiguredFamily()
    {
        _maxio.ReadProductByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(new MaxioProduct
        {
            Handle = ProHandle,
            Name = "Other",
            ProductFamily = new MaxioProductFamily { Handle = "someone-elses-family" }
        });

        await Assert.ThrowsAsync<PlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, ProHandle));
    }

    [Fact]
    public async Task OmitsArchivedProductsFromPlanList()
    {
        _maxio.ListProductsForProductFamilyAsync(FamilyHandle, default).Returns(new List<MaxioProduct>
        {
            ProProduct(),
            new() { Handle = "old-plan", Name = "Old", ArchivedAt = System.DateTimeOffset.UtcNow, PriceInCents = 100 },
            new() { Handle = null, Name = "No handle", PriceInCents = 100 }
        });

        var plans = await CreateSut().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(ProHandle, plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListsSubscriptionsForExistingCustomer()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { ActiveProSubscription(99, 42) });

        var subscriptions = await CreateSut().ListSubscriptionsAsync(_shopper);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(99, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(ProHandle, subscription.PlanHandle);
    }

    [Fact]
    public async Task ReturnsEmptyListWhenShopperHasNoMaxioCustomer()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateSut().ListSubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static MaxioProduct ProProduct() => new()
    {
        Id = 1,
        Handle = ProHandle,
        Name = "Pro Plan",
        Description = "Monthly Pro",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Handle = FamilyHandle }
    };

    private static MaxioSubscription ActiveProSubscription(int id, int customerId) => new()
    {
        Id = id,
        State = "active",
        Reference = "user-1:eshop-pro",
        ProductPriceInCents = 29900,
        NextAssessmentAt = System.DateTimeOffset.UtcNow.AddMonths(1),
        CurrentPeriodEndsAt = System.DateTimeOffset.UtcNow.AddMonths(1),
        Product = ProProduct(),
        Customer = new MaxioCustomer { Id = customerId }
    };
}
