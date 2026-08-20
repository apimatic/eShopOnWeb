using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly SubscriptionEnrollmentGate _gate = new();
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };

    private static readonly ShopperIdentity Shopper = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "eShopOnWeb"
    };

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<BillingProduct> { ProPlan() });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(LiveProSubscription());

        var service = CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<BillingSubscriptionDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<BillingProduct> { ProPlan() });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<BillingCustomerDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<BillingSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<BillingSubscriptionDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LiveProSubscription());

        var service = CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<BillingCustomerDraft>(draft => draft.Reference == "user-1" && draft.Email == Shopper.Email),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<BillingSubscriptionDraft>(draft =>
                draft.CustomerId == 42
                && draft.ProductHandle == "eshop-pro"
                && draft.Reference == "eshop:user-1:eshop-pro"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_RecoversFromDuplicateSubmissionConflict()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<BillingProduct> { ProPlan() });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, (BillingSubscription?)null, LiveProSubscription());
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<BillingSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<BillingSubscriptionDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BillingSubscription>(_ => throw new MaxioDuplicateSubmissionException("duplicate"));

        var service = CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlanHandle()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<BillingProduct> { ProPlan() });

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Shopper, "not-a-plan"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<BillingSubscriptionDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListShopperSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var service = CreateService();

        var result = await service.ListShopperSubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private SubscriptionBillingService CreateService() =>
        new(_maxio, Options.Create(_options), _gate, NullLogger<SubscriptionBillingService>.Instance);

    private static BillingProduct ProPlan() => new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static BillingSubscription LiveProSubscription() => new()
    {
        Id = 99,
        State = "active",
        ProductHandle = "eshop-pro",
        ProductName = "Pro Plan",
        ProductPriceInCents = 29900,
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
