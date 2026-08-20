using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
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
    private readonly ShopperIdentity _shopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly BillingPlan _proPlan = new("eshop-pro", "Pro Plan", "Pro", 29900, 1, "month");

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<BillingPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(_shopper.UserId, _shopper.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email, "demouser", "eShopOnWeb"));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<BillingSubscription>());
        var created = new BillingSubscription(99, "active", "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow.AddMonths(1), null, null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(created);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(_shopper.UserId, _shopper.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<BillingPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email, "demouser", "eShopOnWeb"));
        var existing = new BillingSubscription(99, "active", "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow.AddMonths(1), null, null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<BillingSubscription> { existing });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenMaxioReportsDuplicateSubmission()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<BillingPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(42, _shopper.UserId, _shopper.Email, "demouser", "eShopOnWeb"));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                new List<BillingSubscription>(),
                new List<BillingSubscription>
                {
                    new(99, "active", "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow.AddMonths(1), null, null)
                });
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BillingSubscription>(_ => throw new MaxioDuplicateSubmissionException());

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsUnknownProductHandle()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<BillingPlan> { _proPlan });
        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(
            () => service.SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenNoMaxioCustomerExists()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        var service = new SubscriptionBillingService(_maxio, _logger);

        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SplitDisplayNameUsesEmailLocalPart()
    {
        var (first, last) = SubscriptionBillingService.SplitDisplayName(_shopper);
        Assert.Equal("demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public void UniquenessTokenIsStableForTheSameShopperAndPlan()
    {
        var first = SubscriptionBillingService.CreateUniquenessToken("user-123", "eshop-pro");
        var second = SubscriptionBillingService.CreateUniquenessToken("user-123", "eshop-pro");
        var other = SubscriptionBillingService.CreateUniquenessToken("user-123", "basic-plan");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.True(Guid.TryParse(first, out _));
    }
}
