using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsageAsync
{
    private const string BuyerId = "buyer@example.com";
    private const string OtherBuyerId = "someone-else@example.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private static Subscription MakeSubscription(int id, string buyerId, SubscriptionStatus status) => new(
        id, buyerId, buyerId, "eshop-pro", "Pro Plan", 29900, status, null, null, false, null, null);

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    [Fact]
    public async Task RecordsUsage_WhenSubscriptionIsActiveAndOwnedByCaller()
    {
        var subscription = MakeSubscription(1, BuyerId, SubscriptionStatus.Active);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);
        var summary = new UsageSummary(1, "api-call", 5, "memo", 42);
        _billingClient.RecordUsageAsync(1, 5, "memo").Returns(summary);

        var sut = CreateSut();
        var result = await sut.RecordUsageAsync(1, BuyerId, isAdmin: false, 5, "memo");

        Assert.Equal(42, result.PeriodToDateTotal);
        await _billingClient.Received(1).EnsureMeteredComponentConfiguredAsync();
    }

    [Fact]
    public async Task Throws_WhenSubscriptionNotOwnedByCallerAndNotAdmin()
    {
        var subscription = MakeSubscription(1, OtherBuyerId, SubscriptionStatus.Active);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);

        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedSubscriptionAccessException>(
            () => sut.RecordUsageAsync(1, BuyerId, isAdmin: false, 1, null));
    }

    [Fact]
    public async Task AdminMayRecordUsage_OnAnySubscription()
    {
        var subscription = MakeSubscription(1, OtherBuyerId, SubscriptionStatus.Active);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);
        var summary = new UsageSummary(1, "api-call", 1, null, 1);
        _billingClient.RecordUsageAsync(1, 1, null).Returns(summary);

        var sut = CreateSut();
        var result = await sut.RecordUsageAsync(1, BuyerId, isAdmin: true, 1, null);

        Assert.Equal(1, result.QuantityRecorded);
    }

    [Fact]
    public async Task Throws_WhenSubscriptionIsNotActive()
    {
        var subscription = MakeSubscription(1, BuyerId, SubscriptionStatus.Paused);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.RecordUsageAsync(1, BuyerId, isAdmin: false, 1, null));
    }

    [Fact]
    public async Task Throws_WhenQuantityIsZeroOrNegative()
    {
        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sut.RecordUsageAsync(1, BuyerId, isAdmin: false, 0, null));
        await _billingClient.DidNotReceive().GetSubscriptionAsync(Arg.Any<int>());
    }
}
