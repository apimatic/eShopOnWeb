using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "cp-exp-1",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly Shopper _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new("eshop-pro", "Pro Plan", "Pro", 299m, 1, "month", "eshop-subscribe");

    private SubscriptionBillingService CreateSut() => new(_maxio, _logger, _settings);

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        _maxio.GetProductByHandleAsync("eshop-pro", default).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default)
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((BillingSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(Array.Empty<BillingSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns(new BillingSubscription(99, "eshop-pro", "Pro Plan", 299m, "active", DateTimeOffset.UtcNow.AddMonths(1), "eshop:user-1:eshop-pro", "eshop-subscribe"));

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateBillingCustomer>(c => c.Reference == _shopper.UserId && c.Email == _shopper.Email),
            default);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscription>(s => s.ProductHandle == "eshop-pro" && s.CustomerId == 42),
            default);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        var existing = new BillingSubscription(99, "eshop-pro", "Pro Plan", 299m, "active", DateTimeOffset.UtcNow.AddMonths(1), "eshop:user-1:eshop-pro", "eshop-subscribe");
        _maxio.GetProductByHandleAsync("eshop-pro", default).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default)
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns(existing);

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default);
    }

    [Fact]
    public async Task Subscribe_ReusesCustomer_OnDuplicateReference()
    {
        _maxio.GetProductByHandleAsync("eshop-pro", default).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default)
            .Returns((BillingCustomer?)null, new BillingCustomer(42, _shopper.UserId, _shopper.Email));
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default)
            .Returns<BillingCustomer>(_ => throw new MaxioValidationException(new[] { "Reference: has already been taken" }));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((BillingSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(Array.Empty<BillingSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns(new BillingSubscription(99, "eshop-pro", "Pro Plan", 299m, "active", DateTimeOffset.UtcNow.AddMonths(1), null, "eshop-subscribe"));

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<CreateBillingSubscription>(s => s.CustomerId == 42), default);
    }

    [Fact]
    public async Task Subscribe_RejectsPlanOutsideConfiguredFamily()
    {
        _maxio.GetProductByHandleAsync("other-plan", default)
            .Returns(new SubscriptionPlan("other-plan", "Other", null, 10m, 1, "month", "someone-else"));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, "other-plan"));
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenNoMaxioCustomer()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default).Returns((BillingCustomer?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper.UserId);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task Subscribe_RecoversFromUniquenessConflict()
    {
        var recovered = new BillingSubscription(77, "eshop-pro", "Pro Plan", 299m, "active", DateTimeOffset.UtcNow.AddMonths(1), "eshop:user-1:eshop-pro", "eshop-subscribe");
        _maxio.GetProductByHandleAsync("eshop-pro", default).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default)
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default)
            .Returns((BillingSubscription?)null, recovered);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(Array.Empty<BillingSubscription>(), new[] { recovered });
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns<BillingSubscription>(_ => throw new MaxioDuplicateException(new[] { "DuplicatePrevention::DuplicateSubmissionError" }));

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
    }
}
