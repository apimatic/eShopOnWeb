using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.Services.SubscriptionServiceTests;

public class LifecycleTransitions
{
    private const int SUBSCRIPTION_ID = SubscriptionBuilder.TEST_SUBSCRIPTION_ID;

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _subscriptionService;

    public LifecycleTransitions()
    {
        _subscriptionService = new SubscriptionService(_billingClient, _publisher,
            Substitute.For<IAppLogger<SubscriptionService>>(),
            new SubscriptionSettings { ProductFamilyHandle = "eshop-subscribe", MeteredComponentHandle = "api-call" });
    }

    [Fact]
    public async Task PausesAnActiveSubscription()
    {
        GivenCurrentState(SubscriptionState.Active);
        _billingClient.PauseSubscriptionAsync(SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.OnHold));

        var paused = await _subscriptionService.PauseAsync(SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionState.OnHold, paused.State);
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        GivenCurrentState(SubscriptionState.OnHold);
        _billingClient.ResumeSubscriptionAsync(SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active));

        var resumed = await _subscriptionService.ResumeAsync(SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionState.Active, resumed.State);
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        GivenCurrentState(SubscriptionState.Canceled);
        _billingClient.ReactivateSubscriptionAsync(SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active));

        var reactivated = await _subscriptionService.ReactivateAsync(SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionState.Active, reactivated.State);
    }

    [Fact]
    public async Task CancelsAnActiveSubscriptionAtTheRequestedTime()
    {
        GivenCurrentState(SubscriptionState.Active);
        _billingClient.CancelSubscriptionAsync(SUBSCRIPTION_ID, CancellationTiming.EndOfPeriod, "too pricey",
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active));

        await _subscriptionService.CancelAsync(SUBSCRIPTION_ID, CancellationTiming.EndOfPeriod, "too pricey");

        await _billingClient.Received(1).CancelSubscriptionAsync(SUBSCRIPTION_ID, CancellationTiming.EndOfPeriod,
            "too pricey", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.OnHold)]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RefusesToPauseASubscriptionThatIsNotLive(SubscriptionState state)
    {
        GivenCurrentState(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _subscriptionService.PauseAsync(SUBSCRIPTION_ID));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal("pause", exception.Action);
        await _billingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.Canceled)]
    public async Task RefusesToResumeASubscriptionThatIsNotOnHold(SubscriptionState state)
    {
        GivenCurrentState(state);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _subscriptionService.ResumeAsync(SUBSCRIPTION_ID));

        await _billingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.OnHold)]
    public async Task RefusesToReactivateASubscriptionThatHasNotEnded(SubscriptionState state)
    {
        GivenCurrentState(state);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _subscriptionService.ReactivateAsync(SUBSCRIPTION_ID));

        await _billingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RefusesToCancelASubscriptionThatHasAlreadyEnded(SubscriptionState state)
    {
        GivenCurrentState(state);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _subscriptionService.CancelAsync(SUBSCRIPTION_ID, CancellationTiming.Immediately, null));

        await _billingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesALifecycleActionOnASubscriptionThatDoesNotExist()
    {
        _billingClient.GetSubscriptionAsync(999999, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => _subscriptionService.PauseAsync(999999));
    }

    [Fact]
    public async Task AnnouncesTheTransitionCarryingTheOldAndNewStates()
    {
        GivenCurrentState(SubscriptionState.Active);
        _billingClient.PauseSubscriptionAsync(SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.OnHold));

        await _subscriptionService.PauseAsync(SUBSCRIPTION_ID);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(changed =>
                changed.OldState == SubscriptionState.Active
                && changed.NewState == SubscriptionState.OnHold
                && changed.Action == "pause"
                && changed.SubscriptionId == SUBSCRIPTION_ID),
            Arg.Any<CancellationToken>());
    }

    private void GivenCurrentState(SubscriptionState state)
    {
        _billingClient.GetSubscriptionAsync(SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(state));
    }
}
