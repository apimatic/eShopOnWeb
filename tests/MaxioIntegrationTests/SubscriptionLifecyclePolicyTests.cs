using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The lifecycle state machine that decides which transitions may even be attempted (plan.md UC4).
/// </summary>
public class SubscriptionLifecyclePolicyTests
{
    private static Subscription With(SubscriptionState state, bool cancelAtEndOfPeriod = false) => new()
    {
        Id = 1,
        State = state,
        CancelAtEndOfPeriod = cancelAtEndOfPeriod
    };

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.Trialing)]
    public void ALiveSubscriptionCanBePausedOrCancelledEitherWay(SubscriptionState state)
    {
        var allowed = SubscriptionLifecyclePolicy.AllowedActions(With(state));

        Assert.Contains(SubscriptionLifecycleAction.Pause, allowed);
        Assert.Contains(SubscriptionLifecycleAction.Cancel, allowed);
        Assert.Contains(SubscriptionLifecycleAction.CancelAtEndOfPeriod, allowed);
        Assert.DoesNotContain(SubscriptionLifecycleAction.Resume, allowed);
        Assert.DoesNotContain(SubscriptionLifecycleAction.Reactivate, allowed);
    }

    [Fact]
    public void APendingEndOfPeriodCancelIsNotOfferedTwice()
    {
        var allowed = SubscriptionLifecyclePolicy.AllowedActions(
            With(SubscriptionState.Active, cancelAtEndOfPeriod: true));

        Assert.DoesNotContain(SubscriptionLifecycleAction.CancelAtEndOfPeriod, allowed);
        Assert.Contains(SubscriptionLifecycleAction.Cancel, allowed);
    }

    [Fact]
    public void APausedSubscriptionCanOnlyBeResumedOrCancelled()
    {
        var allowed = SubscriptionLifecyclePolicy.AllowedActions(With(SubscriptionState.Paused));

        Assert.Equal(
            new[] { SubscriptionLifecycleAction.Resume, SubscriptionLifecycleAction.Cancel },
            allowed);
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Expired)]
    [InlineData(SubscriptionState.TrialEnded)]
    [InlineData(SubscriptionState.Unpaid)]
    public void AnEndedSubscriptionCanOnlyBeReactivated(SubscriptionState state)
    {
        var allowed = SubscriptionLifecyclePolicy.AllowedActions(With(state));

        Assert.Equal(new[] { SubscriptionLifecycleAction.Reactivate }, allowed);
    }

    [Theory]
    [InlineData(SubscriptionState.Suspended)]
    [InlineData(SubscriptionState.Failed)]
    [InlineData(SubscriptionState.Unknown)]
    public void AStateTheProviderControlsOffersNoTransitions(SubscriptionState state)
    {
        Assert.Empty(SubscriptionLifecyclePolicy.AllowedActions(With(state)));
    }

    [Fact]
    public void APastDueSubscriptionCanBeCancelledButNotPaused()
    {
        var allowed = SubscriptionLifecyclePolicy.AllowedActions(With(SubscriptionState.PastDue));

        Assert.Contains(SubscriptionLifecycleAction.Cancel, allowed);
        Assert.DoesNotContain(SubscriptionLifecycleAction.Pause, allowed);
    }

    [Fact]
    public void EnsureAllowed_NamesTheCurrentStateAndTheAlternatives()
    {
        var subscription = With(SubscriptionState.Canceled);

        var exception = Assert.Throws<InvalidSubscriptionTransitionException>(
            () => SubscriptionLifecyclePolicy.EnsureAllowed(subscription, SubscriptionLifecycleAction.Pause));

        Assert.Contains("Canceled", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Reactivate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_PassesForALegalTransition()
    {
        SubscriptionLifecyclePolicy.EnsureAllowed(With(SubscriptionState.Active), SubscriptionLifecycleAction.Pause);

        Assert.True(SubscriptionLifecyclePolicy.IsAllowed(
            With(SubscriptionState.Active), SubscriptionLifecycleAction.Pause));
    }
}
