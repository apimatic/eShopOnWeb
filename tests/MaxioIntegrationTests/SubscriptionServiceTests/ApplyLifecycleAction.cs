using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ApplyLifecycleAction : SubscriptionServiceFixture
{
    [Fact]
    public async Task PausesAnActiveSubscriptionAndPublishesTheStateChange()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());
        BillingClient.PauseSubscriptionAsync(42, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.OnHold));

        var updated = await Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.OnHold, updated.State);
        await Publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(changed =>
                changed.OldState == SubscriptionState.Active
                && changed.NewState == SubscriptionState.OnHold
                && changed.Action == SubscriptionLifecycleAction.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.OnHold));
        BillingClient.ResumeSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());

        var updated = await Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Canceled));
        BillingClient.ReactivateSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());

        var updated = await Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task CancelsAtEndOfPeriodAndReportsTheEffectiveDate()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());
        BillingClient.CancelSubscriptionAsync(42, CancellationTiming.EndOfPeriod, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(cancelAtEndOfPeriod: true));

        await Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel,
            CancellationTiming.EndOfPeriod);

        // A deferred cancellation takes effect at the period boundary, not now.
        await Publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(changed =>
                changed.EffectiveAt == new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.OnHold, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel)]
    public async Task RejectsAnIllegalTransitionWithoutCallingTheProvider(SubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription(state: state));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, action));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal(action, exception.RequestedAction);
        Assert.DoesNotContain(action, exception.AllowedActions);

        await BillingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await BillingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await BillingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await BillingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await Publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectionNamesTheLegalAlternatives()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Pause));

        Assert.Contains(SubscriptionLifecycleAction.Reactivate, exception.AllowedActions);
        Assert.Contains("Reactivate", exception.Message);
    }

    [Fact]
    public async Task RejectsAnEndOfPeriodCancellationWhilePastDue()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.PastDue));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.EndOfPeriod));
    }

    [Fact]
    public async Task StillAllowsAnImmediateCancellationWhilePastDue()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.PastDue));
        BillingClient.CancelSubscriptionAsync(42, CancellationTiming.Immediate, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Canceled));

        var updated = await Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel);

        Assert.Equal(SubscriptionState.Canceled, updated.State);
    }

    [Fact]
    public async Task RejectsASecondEndOfPeriodCancellationWhileOneIsPending()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(cancelAtEndOfPeriod: true));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.EndOfPeriod));
    }

    [Fact]
    public async Task RefusesToActOnSomebodyElsesSubscription()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(userReference: OtherUserReference));

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel));

        await BillingClient.DidNotReceive().CancelSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<CancellationTiming>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAnUnknownSubscription()
    {
        BillingClient.GetSubscriptionAsync(999, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 999, SubscriptionLifecycleAction.Cancel));
    }

    [Fact]
    public async Task AdminSurfaceActsOnASubscriptionOwnedByAnotherUser()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(userReference: OtherUserReference));
        BillingClient.CancelSubscriptionAsync(42, CancellationTiming.Immediate, "fraud", Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Canceled, userReference: OtherUserReference));

        var updated = await Service.ApplyLifecycleActionForSubscriptionAsync(42,
            SubscriptionLifecycleAction.Cancel, CancellationTiming.Immediate, "fraud");

        Assert.Equal(SubscriptionState.Canceled, updated.State);

        // The notification is attributed to the subscription's real owner, not the acting admin.
        await Publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(changed => changed.UserReference == OtherUserReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeverActsOnASubscriptionInAnUnknownState()
    {
        // An unmodelled provider state must not be treated as actionable.
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Unknown));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ApplyLifecycleActionAsync(UserReference, 42, SubscriptionLifecycleAction.Cancel));
    }
}
