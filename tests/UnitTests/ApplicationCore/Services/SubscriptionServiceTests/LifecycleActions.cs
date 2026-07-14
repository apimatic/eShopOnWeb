using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class LifecycleActions
{
    private const string BuyerId = "buyer@example.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private static Subscription MakeSubscription(SubscriptionStatus status, bool cancelAtEndOfPeriod = false) => new(
        1, BuyerId, BuyerId, "eshop-pro", "Pro Plan", 29900, status, null, null, cancelAtEndOfPeriod, null, null);

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    [Fact]
    public async Task Pause_Throws_WhenSubscriptionIsNotActive()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.OnHold));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.PauseSubscriptionAsync(1, BuyerId, isAdmin: false));
    }

    [Fact]
    public async Task Resume_Throws_WhenSubscriptionIsNotPaused()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Active));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.ResumeSubscriptionAsync(1, BuyerId, isAdmin: false));
    }

    [Fact]
    public async Task Reactivate_Throws_WhenSubscriptionIsActive()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Active));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.ReactivateSubscriptionAsync(1, BuyerId, isAdmin: false));
    }

    [Fact]
    public async Task Cancel_Throws_WhenSubscriptionAlreadyCancelled()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Canceled));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.CancelSubscriptionAsync(1, BuyerId, isAdmin: false, CancellationTiming.Immediate, null));
    }

    [Fact]
    public async Task Cancel_ReturnsCurrentState_WhenEndOfPeriodCancelAlreadyPending()
    {
        var subscription = MakeSubscription(SubscriptionStatus.Active, cancelAtEndOfPeriod: true);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);
        var sut = CreateSut();

        var result = await sut.CancelSubscriptionAsync(1, BuyerId, isAdmin: false, CancellationTiming.EndOfPeriod, null);

        Assert.True(result.CancelAtEndOfPeriod);
        await _billingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>());
    }
}
