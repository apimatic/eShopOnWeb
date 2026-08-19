using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IAdvancedBillingGateway _gateway = Substitute.For<IAdvancedBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscriptionBillingService _service;
    private readonly BillingShopper _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    public Subscribe()
    {
        _service = new SubscriptionBillingService(_gateway, new UserKeyedLock(), _logger);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _gateway.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Plan("eshop-pro", 29900));
        _gateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1", Email = _shopper.Email, FirstName = "Demouser", LastName = "Microsoft" });
        _gateway.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(1001, "eshop-pro", 29900, "user-1:eshop-pro"));

        var result = await _service.SubscribeAsync(_shopper, "eshop-pro", CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(1001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<CreateBillingCustomer>(c => c.Reference == "user-1" && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscription>(s =>
                s.ProductHandle == "eshop-pro"
                && s.CustomerId == 42
                && s.Reference == "user-1:eshop-pro"
                && s.PaymentCollectionMethod == SubscriptionBillingService.RemittanceCollectionMethod),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWithoutCreatingAnother()
    {
        _gateway.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Plan("eshop-pro", 29900));
        _gateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1", Email = _shopper.Email });
        _gateway.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(1001, "eshop-pro", 29900, "user-1:eshop-pro"));

        var result = await _service.SubscribeAsync(_shopper, "eshop-pro", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(1001, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnknownProductHandle()
    {
        _gateway.ReadProductByHandleAsync("missing-plan", Arg.Any<CancellationToken>())
            .Returns((BillingProduct?)null);

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            _service.SubscribeAsync(_shopper, "missing-plan", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsBlankProductHandle()
    {
        await Assert.ThrowsAsync<BillingValidationException>(() =>
            _service.SubscribeAsync(_shopper, "  ", CancellationToken.None));
    }

    private static BillingProduct Plan(string handle, long cents) => new()
    {
        Id = 7,
        Handle = handle,
        Name = "Pro Plan",
        PriceInCents = cents,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static BillingSubscription ActiveSubscription(int id, string handle, long cents, string reference) => new()
    {
        Id = id,
        State = "active",
        Reference = reference,
        ProductPriceInCents = cents,
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = Plan(handle, cents)
    };
}
