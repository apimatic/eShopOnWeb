using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The orchestration above the provider seam: idempotent subscribe, ownership, the usage guards, the
/// stale-preview rule, the lifecycle state machine, and best-effort in-process eventing.
/// </summary>
public class SubscriptionServiceTests
{
    private const string User = "demouser@microsoft.com";
    private const string OtherUser = "someoneelse@microsoft.com";

    private readonly FakeBillingClient _billing = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly RecordingLogger<SubscriptionService> _logger = new();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _billing.Plans.Add(new SubscriptionPlan
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29_900,
            Interval = 1,
            IntervalUnit = "month"
        });

        _billing.Plans.Add(new SubscriptionPlan
        {
            Handle = "basic-plan",
            Name = "Basic Plan",
            PriceInCents = 2_900,
            Interval = 1,
            IntervalUnit = "month"
        });

        _service = new SubscriptionService(_billing, _publisher, _logger);
    }

    private Subscription GivenSubscription(
        int id = 60001,
        string owner = User,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = "eshop-pro",
        bool cancelAtEndOfPeriod = false)
    {
        var customer = new BillingCustomer { Id = 500123, Reference = owner, Email = owner };
        _billing.CustomersByReference[owner] = customer;

        var subscription = new Subscription
        {
            Id = id,
            CustomerId = customer.Id,
            CustomerReference = owner,
            State = state,
            PlanHandle = planHandle,
            PlanName = planHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
            PlanPriceInCents = planHandle == "eshop-pro" ? 29_900 : 2_900,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow.AddDays(-10),
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddDays(20),
            CancelAtEndOfPeriod = cancelAtEndOfPeriod
        };

        _billing.Add(subscription);
        return subscription;
    }

    // -- UC1 subscribe -----------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_EnrolsTheUser_AndPublishesSubscriptionActivated()
    {
        var subscription = await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(User, subscription.CustomerReference);

        var activated = _publisher.Single<SubscriptionActivated>();
        Assert.Equal(subscription.Id, activated.SubscriptionId);
        Assert.Equal(User, activated.CustomerReference);
        Assert.Equal("eshop-pro", activated.PlanHandle);
        Assert.Equal(299.00m, activated.PlanPrice);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_ReturningTheLiveSubscriptionInsteadOfEnrollingTwice()
    {
        var existing = GivenSubscription();

        var result = await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.CreateSubscriptionAsync)));
        Assert.False(_publisher.PublishedAny<SubscriptionActivated>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsAPausedSubscription_RatherThanCreatingASecondOne()
    {
        var existing = GivenSubscription(state: SubscriptionState.Paused);

        var result = await _service.SubscribeAsync(User, "basic-plan");

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.CreateSubscriptionAsync)));
    }

    [Fact]
    public async Task SubscribeAsync_EnrolsAgain_WhenTheOnlyPriorSubscriptionWasCancelled()
    {
        GivenSubscription(state: SubscriptionState.Canceled);

        var result = await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(SubscriptionState.Active, result.State);
        Assert.Equal(1, _billing.CountOf(nameof(FakeBillingClient.CreateSubscriptionAsync)));
    }

    [Fact]
    public async Task SubscribeAsync_RefusesAnUnresolvableHandle_WithoutCreatingACustomer()
    {
        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(User, "no-such-plan"));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.EnsureCustomerAsync)));
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.CreateSubscriptionAsync)));
    }

    [Fact]
    public async Task SubscribeAsync_StillSucceeds_WhenAnInProcessHandlerThrows()
    {
        _publisher.ThrowOnPublish = new InvalidOperationException("the email handler blew up");

        var subscription = await _service.SubscribeAsync(User, "eshop-pro");

        // Best-effort eventing: the enrolment stands and the handler failure is logged, not rethrown.
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Contains(_logger.Warnings,
            warning => warning.Contains("SubscriptionActivated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_ForAUserTheProviderDoesNotKnow()
    {
        Assert.Empty(await _service.ListSubscriptionsAsync("stranger@example.com"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsTheUsersSubscriptions()
    {
        GivenSubscription();

        var subscriptions = await _service.ListSubscriptionsAsync(User);

        Assert.Equal(60001, Assert.Single(subscriptions).Id);
    }

    // -- Ownership ---------------------------------------------------------------------------------

    [Fact]
    public async Task ACustomerCannotActOnSomeoneElsesSubscription()
    {
        GivenSubscription(owner: OtherUser);

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(
            () => _service.RecordUsageAsync(SubscriptionActor.Customer(User), 60001, 1m, null));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    [Fact]
    public async Task AnAdministratorMayActOnAnySubscription()
    {
        GivenSubscription(owner: OtherUser);

        var report = await _service.RecordUsageAsync(
            SubscriptionActor.Administrator(User), 60001, 3m, "admin adjustment");

        Assert.Equal(3m, report.Record!.Quantity);
    }

    [Fact]
    public async Task AnUnknownSubscriptionIsReportedAsNotFound()
    {
        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _service.GetSubscriptionAsync(SubscriptionActor.Customer(User), 999999));
    }

    // -- UC2 usage ---------------------------------------------------------------------------------

    [Fact]
    public async Task RecordUsageAsync_RecordsTheUnits_AndReportsTheRunningTotalAndItsValue()
    {
        GivenSubscription();
        _billing.PeriodToDateUnits = 9m;

        var report = await _service.RecordUsageAsync(
            SubscriptionActor.Customer(User), 60001, 3m, "three calls");

        Assert.Equal(3m, report.Record!.Quantity);
        Assert.Equal("api-call", report.ComponentHandle);
        Assert.True(report.PeriodToDateAvailable);
        Assert.Equal(12m, report.PeriodToDateUnits);
        Assert.Equal(1L, report.UnitPriceInCents);

        // 12 units at $0.01 each.
        Assert.Equal(0.12m, report.EstimatedPeriodToDateCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantity_BeforeReadingTheSubscription(decimal quantity)
    {
        GivenSubscription();

        await Assert.ThrowsAsync<InvalidUsageQuantityException>(
            () => _service.RecordUsageAsync(SubscriptionActor.Customer(User), 60001, quantity, null));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.GetSubscriptionAsync)));
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    [Theory]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RecordUsageAsync_RefusesASubscriptionThatIsNotActive(SubscriptionState state)
    {
        GivenSubscription(state: state);

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => _service.RecordUsageAsync(SubscriptionActor.Customer(User), 60001, 1m, null));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesAComponentThatIsNotMetered_BeforeRecordingAnything()
    {
        GivenSubscription();
        _billing.Component = _billing.Component with { Kind = "quantity_based_component", IsMetered = false };

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.RecordUsageAsync(SubscriptionActor.Customer(User), 60001, 1m, null));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    [Fact]
    public async Task RecordUsageAsync_KeepsTheUsage_WhenTheRunningTotalCannotBeReadBack()
    {
        GivenSubscription();
        _billing.PeriodToDateFailure = new BillingProviderException("read usage", 503, "provider unavailable");

        var report = await _service.RecordUsageAsync(SubscriptionActor.Customer(User), 60001, 2m, null);

        // The usage stands; only the total is reported unavailable.
        Assert.Equal(2m, report.Record!.Quantity);
        Assert.False(report.PeriodToDateAvailable);
        Assert.Null(report.PeriodToDateUnits);
        Assert.Null(report.EstimatedPeriodToDateCharge);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("could not be read back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordUsageForUserAsync_ReturnsNull_WhenTheBuyerHasNoBillingCustomer()
    {
        Assert.Null(await _service.RecordUsageForUserAsync("stranger@example.com", 1m, "order 1"));
    }

    [Fact]
    public async Task RecordUsageForUserAsync_ReturnsNull_WhenTheBuyerHasNoActiveSubscription()
    {
        GivenSubscription(state: SubscriptionState.Canceled);

        Assert.Null(await _service.RecordUsageForUserAsync(User, 1m, "order 1"));
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    [Fact]
    public async Task RecordUsageForUserAsync_MetersOneUnit_AgainstTheBuyersActiveSubscription()
    {
        GivenSubscription();

        var report = await _service.RecordUsageForUserAsync(User, 1m, "eShopOnWeb order 42");

        Assert.NotNull(report);
        Assert.Equal(1m, report!.Record!.Quantity);
        Assert.Equal("eShopOnWeb order 42", report.Record.Memo);
        Assert.Equal(60001, report.SubscriptionId);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ReportsTheRunningTotal_WithoutRecordingAnything()
    {
        GivenSubscription();
        _billing.PeriodToDateUnits = 250m;

        var summary = await _service.GetUsageSummaryAsync(SubscriptionActor.Customer(User), 60001);

        Assert.Null(summary.Record);
        Assert.Equal(250m, summary.PeriodToDateUnits);
        Assert.Equal(2.50m, summary.EstimatedPeriodToDateCharge);
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.RecordUsageAsync)));
    }

    // -- UC3 plan change ---------------------------------------------------------------------------

    [Fact]
    public async Task PreviewPlanChangeAsync_ReturnsTheProration_WithBothPlansNamed()
    {
        GivenSubscription(planHandle: "basic-plan");

        var preview = await _service.PreviewPlanChangeAsync(
            SubscriptionActor.Customer(User), 60001, "eshop-pro");

        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal("Pro Plan", preview.TargetPlanName);
        Assert.Equal(270.00m, preview.PaymentDue);
    }

    [Fact]
    public async Task PlanChange_ToTheSamePlan_IsRejectedWithoutTouchingTheProvider()
    {
        GivenSubscription(planHandle: "eshop-pro");

        await Assert.ThrowsAsync<InvalidPlanChangeException>(
            () => _service.PreviewPlanChangeAsync(SubscriptionActor.Customer(User), 60001, "eshop-pro"));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.PreviewPlanChangeAsync)));
    }

    [Fact]
    public async Task PlanChange_IsRejected_WhenTheSubscriptionIsCancelled()
    {
        GivenSubscription(state: SubscriptionState.Canceled);

        var exception = await Assert.ThrowsAsync<InvalidPlanChangeException>(
            () => _service.PreviewPlanChangeAsync(SubscriptionActor.Customer(User), 60001, "basic-plan"));

        Assert.Contains("Reactivate", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.PreviewPlanChangeAsync)));
    }

    [Fact]
    public async Task PlanChange_IsRejected_WhenTheTargetHandleDoesNotResolve()
    {
        GivenSubscription();

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.PreviewPlanChangeAsync(SubscriptionActor.Customer(User), 60001, "ghost-plan"));
    }

    [Fact]
    public async Task ChangePlanAsync_AppliesImmediately_WhenTheConfirmedAmountStillMatches()
    {
        GivenSubscription(planHandle: "basic-plan");

        var preview = await _service.PreviewPlanChangeAsync(
            SubscriptionActor.Customer(User), 60001, "eshop-pro");

        var updated = await _service.ChangePlanAsync(
            SubscriptionActor.Customer(User), 60001, PlanChangeRequest.FromConfirmedPreview(preview));

        Assert.Equal("eshop-pro", updated.PlanHandle);
        Assert.Equal(1, _billing.CountOf(nameof(FakeBillingClient.ChangePlanImmediatelyAsync)));

        var published = _publisher.Single<SubscriptionPlanChanged>();
        Assert.Equal("basic-plan", published.PreviousPlanHandle);
        Assert.Equal("eshop-pro", published.NewPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediately, published.Timing);
        Assert.Equal(270.00m, published.PaymentDue);
    }

    [Fact]
    public async Task ChangePlanAsync_RefusesToApply_WhenTheProrationMovedSinceThePreview()
    {
        GivenSubscription(planHandle: "basic-plan");

        var stale = new PlanChangeRequest
        {
            TargetPlanHandle = "eshop-pro",
            Timing = PlanChangeTiming.Immediately,
            ConfirmedPaymentDueInCents = 10_000,
            PreviewedAt = DateTimeOffset.UtcNow
        };

        var exception = await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(SubscriptionActor.Customer(User), 60001, stale));

        Assert.Contains("100.00", exception.Message, StringComparison.Ordinal);
        Assert.Contains("270.00", exception.Message, StringComparison.Ordinal);

        // The customer is never charged an amount they did not see.
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.ChangePlanImmediatelyAsync)));
        Assert.False(_publisher.PublishedAny<SubscriptionPlanChanged>());
    }

    [Fact]
    public async Task ChangePlanAsync_RefusesAPreviewThatHasExpired()
    {
        GivenSubscription(planHandle: "basic-plan");

        var expired = new PlanChangeRequest
        {
            TargetPlanHandle = "eshop-pro",
            Timing = PlanChangeTiming.Immediately,
            ConfirmedPaymentDueInCents = 27_000,
            PreviewedAt = DateTimeOffset.UtcNow - SubscriptionConstants.PreviewValidity - TimeSpan.FromMinutes(1)
        };

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(SubscriptionActor.Customer(User), 60001, expired));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.ChangePlanImmediatelyAsync)));
    }

    [Fact]
    public async Task ChangePlanAsync_RefusesAnImmediateChangeWithNoConfirmedPreview()
    {
        GivenSubscription(planHandle: "basic-plan");

        var unconfirmed = new PlanChangeRequest
        {
            TargetPlanHandle = "eshop-pro",
            Timing = PlanChangeTiming.Immediately
        };

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(SubscriptionActor.Customer(User), 60001, unconfirmed));

        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.ChangePlanImmediatelyAsync)));
    }

    [Fact]
    public async Task ChangePlanAsync_AtNextRenewal_NeedsNoPreview_AndChargesNoProration()
    {
        GivenSubscription(planHandle: "eshop-pro");

        var updated = await _service.ChangePlanAsync(
            SubscriptionActor.Customer(User), 60001, PlanChangeRequest.AtNextRenewalFor("basic-plan"));

        Assert.Equal("eshop-pro", updated.PlanHandle);
        Assert.Equal("basic-plan", updated.ScheduledPlanHandle);
        Assert.Equal(0, _billing.CountOf(nameof(FakeBillingClient.PreviewPlanChangeAsync)));

        var published = _publisher.Single<SubscriptionPlanChanged>();
        Assert.Equal(PlanChangeTiming.AtNextRenewal, published.Timing);
        Assert.Null(published.PaymentDue);
    }

    // -- UC4 lifecycle -----------------------------------------------------------------------------

    [Fact]
    public async Task Pause_MovesAnActiveSubscriptionToPaused_AndPublishesTheTransition()
    {
        GivenSubscription();

        var updated = await _service.ExecuteLifecycleActionAsync(
            SubscriptionActor.Customer(User),
            60001,
            SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.Pause, "on holiday"));

        Assert.Equal(SubscriptionState.Paused, updated.State);

        var published = _publisher.Single<SubscriptionStateChanged>();
        Assert.Equal(SubscriptionState.Active, published.PreviousState);
        Assert.Equal(SubscriptionState.Paused, published.NewState);
        Assert.Equal(SubscriptionLifecycleAction.Pause, published.Action);
    }

    [Fact]
    public async Task Resume_MovesAPausedSubscriptionBackToActive()
    {
        GivenSubscription(state: SubscriptionState.Paused);

        var updated = await _service.ExecuteLifecycleActionAsync(
            SubscriptionActor.Customer(User), 60001,
            SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.Resume));

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task CancelAtEndOfPeriod_SchedulesTheCancellationForThePeriodBoundary()
    {
        var subscription = GivenSubscription();

        var updated = await _service.ExecuteLifecycleActionAsync(
            SubscriptionActor.Customer(User), 60001,
            SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.CancelAtEndOfPeriod, "moving on"));

        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.Equal(subscription.CurrentPeriodEndsAt, updated.ScheduledCancellationAt);
    }

    [Fact]
    public async Task Reactivate_BringsACancelledSubscriptionBack()
    {
        GivenSubscription(state: SubscriptionState.Canceled);

        var updated = await _service.ExecuteLifecycleActionAsync(
            SubscriptionActor.Customer(User), 60001,
            SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.Reactivate));

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Resume)]
    public async Task AnIllegalTransitionIsRejectedLocally_WithNoProviderCall(
        SubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        GivenSubscription(state: state);
        var callsBefore = _billing.CallCount;

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.ExecuteLifecycleActionAsync(
                SubscriptionActor.Customer(User), 60001, SubscriptionLifecycleRequest.For(action)));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal(action, exception.RequestedAction);
        Assert.DoesNotContain(action, exception.AllowedActions);

        // Only the read that loaded the subscription happened.
        Assert.Equal(callsBefore + 1, _billing.CallCount);
        Assert.False(_publisher.PublishedAny<SubscriptionStateChanged>());
    }

    [Fact]
    public async Task ASecondEndOfPeriodCancel_IsRejected_RatherThanReportedAsNewlyApplied()
    {
        GivenSubscription(cancelAtEndOfPeriod: true);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.ExecuteLifecycleActionAsync(
                SubscriptionActor.Customer(User), 60001,
                SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.CancelAtEndOfPeriod)));

        Assert.DoesNotContain(SubscriptionLifecycleAction.CancelAtEndOfPeriod, exception.AllowedActions);

        // An immediate cancel is still offered as the way out.
        Assert.Contains(SubscriptionLifecycleAction.Cancel, exception.AllowedActions);
    }

    [Fact]
    public async Task ALifecycleTransitionStands_EvenWhenAnInProcessHandlerThrows()
    {
        GivenSubscription();
        _publisher.ThrowOnPublish = new InvalidOperationException("audit handler failed");

        var updated = await _service.ExecuteLifecycleActionAsync(
            SubscriptionActor.Customer(User), 60001,
            SubscriptionLifecycleRequest.For(SubscriptionLifecycleAction.Pause));

        Assert.Equal(SubscriptionState.Paused, updated.State);
    }
}
