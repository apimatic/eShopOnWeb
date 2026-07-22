using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>
/// UC4 — the lifecycle actions. An illegal transition must be rejected locally, with the current
/// state and the legal alternatives, and without touching the provider.
/// </summary>
public class LifecycleActions
{
    private const string UserReference = "demouser@microsoft.com";
    private const int CustomerId = 90210;

    private static readonly BillingPlan ProPlan = new(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month");

    private static (SubscriptionService Service, FakeBillingClient Billing, RecordingPublisher Publisher) Build(
        SubscriptionState state)
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(ProPlan);
        billing.Customer = new BillingCustomer(CustomerId, UserReference, UserReference);
        billing.Subscriptions.Add(new Subscription(50, UserReference, CustomerId, ProPlan, state,
            state.ToString().ToLowerInvariant()));

        var publisher = new RecordingPublisher();
        return (new SubscriptionService(billing, publisher, new RecordingLogger<SubscriptionService>()),
            billing, publisher);
    }

    [Fact]
    public async Task PausingAnActiveSubscriptionPutsItOnHoldAndAnnouncesTheTransition()
    {
        var (service, _, publisher) = Build(SubscriptionState.Active);

        var subscription = await service.PauseAsync(50, UserReference);

        Assert.Equal(SubscriptionState.Paused, subscription.State);

        var notification = publisher.Single<SubscriptionStateChanged>();
        Assert.Equal(SubscriptionState.Active, notification.PreviousState);
        Assert.Equal(SubscriptionState.Paused, notification.NewState);
        Assert.Equal("pause", notification.Action);
    }

    [Fact]
    public async Task ResumingAPausedSubscriptionMakesItActiveAgain()
    {
        var (service, _, publisher) = Build(SubscriptionState.Paused);

        var subscription = await service.ResumeAsync(50, UserReference);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("resume", publisher.Single<SubscriptionStateChanged>().Action);
    }

    [Fact]
    public async Task CancellingImmediatelyEndsTheSubscription()
    {
        var (service, _, publisher) = Build(SubscriptionState.Active);

        var subscription = await service.CancelAsync(50, CancellationTiming.Immediate, "Too expensive", UserReference);

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.Contains("cancel", publisher.Single<SubscriptionStateChanged>().Action);
    }

    [Fact]
    public async Task CancellingAtEndOfPeriodKeepsTheSubscriptionRunningUntilTheBoundary()
    {
        var (service, billing, _) = Build(SubscriptionState.Active);

        var subscription = await service.CancelAsync(50, CancellationTiming.EndOfPeriod, null, UserReference);

        // The customer keeps access they have paid for; only the schedule changes.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Contains($"Cancel:50:{CancellationTiming.EndOfPeriod}", billing.Calls);
    }

    [Fact]
    public async Task ReactivatingACancelledSubscriptionBringsItBack()
    {
        var (service, _, publisher) = Build(SubscriptionState.Canceled);

        var subscription = await service.ReactivateAsync(50, UserReference);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("reactivate", publisher.Single<SubscriptionStateChanged>().Action);
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RefusesToPauseASubscriptionThatIsNotBilling(SubscriptionState state)
    {
        var (service, billing, publisher) = Build(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.PauseAsync(50, UserReference));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal("pause", exception.Action);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Pause:", StringComparison.Ordinal));
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.Canceled)]
    public async Task RefusesToResumeASubscriptionThatIsNotPaused(SubscriptionState state)
    {
        var (service, billing, _) = Build(state);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(() => service.ResumeAsync(50, UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Resume:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.Paused)]
    public async Task RefusesToReactivateASubscriptionThatHasNotEnded(SubscriptionState state)
    {
        var (service, billing, _) = Build(state);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.ReactivateAsync(50, UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Reactivate:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesToCancelASubscriptionThatHasAlreadyEnded()
    {
        var (service, billing, _) = Build(SubscriptionState.Canceled);

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.CancelAsync(50, CancellationTiming.Immediate, null, UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Cancel:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TellsTheActorWhichTransitionsAreLegalFromTheCurrentState()
    {
        var (service, _, _) = Build(SubscriptionState.Paused);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.PauseAsync(50, UserReference));

        // A bare rejection is not actionable; the legal alternatives must come with it.
        Assert.Contains("resume", exception.LegalActions);
        Assert.Contains("resume", exception.Message);
    }

    [Fact]
    public async Task RefusesToActOnAnotherCustomersSubscription()
    {
        var (service, billing, _) = Build(SubscriptionState.Active);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.CancelAsync(50, CancellationTiming.Immediate, null, "someone.else@example.com"));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Cancel:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsAnUnknownSubscriptionTheSameWayAsOneOwnedBySomeoneElse()
    {
        var (service, _, _) = Build(SubscriptionState.Active);

        var missing = await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.GetSubscriptionAsync(9999, UserReference));
        var someoneElses = await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.GetSubscriptionAsync(50, "someone.else@example.com"));

        // Identical messages: otherwise an authenticated user could probe for which ids exist.
        Assert.Equal(missing.GetType(), someoneElses.GetType());
        Assert.Equal("No subscription found with id 50", someoneElses.Message);
    }

    [Fact]
    public async Task AllowsAnAdministratorToActOnAnySubscription()
    {
        var (service, _, _) = Build(SubscriptionState.Active);

        var subscription = await service.PauseAsync(50, actingUserReference: null);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
    }

    [Fact]
    public async Task KeepsTheStateChangeWhenNotificationDeliveryFails()
    {
        var (service, _, publisher) = Build(SubscriptionState.Active);
        publisher.Failure = new InvalidOperationException("a handler blew up");

        var subscription = await service.PauseAsync(50, UserReference);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
    }

    [Fact]
    public async Task ListsNoSubscriptionsForAUserWhoHasNeverEnrolled()
    {
        var billing = new FakeBillingClient();
        var service = new SubscriptionService(billing, new RecordingPublisher(),
            new RecordingLogger<SubscriptionService>());

        var subscriptions = await service.ListSubscriptionsForUserAsync("nobody@example.com");

        // No provider-side customer is not an error, it just means nothing to show.
        Assert.Empty(subscriptions);
    }
}
