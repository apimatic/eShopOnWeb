using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The orchestration over the provider seam: the guarantees the use cases make about
/// idempotency, ownership, validation ordering, and best-effort eventing.
/// </summary>
public class SubscriptionServiceTests
{
    private const string Owner = "owner@microsoft.com";
    private const string Stranger = "stranger@microsoft.com";

    private readonly FakeBillingClient _billing = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly TestAppLogger<SubscriptionService> _logger = new();

    private SubscriptionService Service => new(_billing, _publisher, _logger);

    // ---------- UC1: subscribe ----------

    [Fact]
    public async Task SubscribeAsync_EnrolsTheUserAndAnnouncesIt()
    {
        var subscription = await Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "eshop-pro");

        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(1, _billing.CreatedSubscriptionCount);

        var activated = _publisher.Single<SubscriptionActivated>();
        Assert.Equal(Owner, activated.UserName);
        Assert.Equal(subscription.Id, activated.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var first = await Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "eshop-pro");
        var second = await Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "eshop-pro");

        Assert.Equal(first.Id, second.Id);

        // The double-click must not produce a second enrollment or a second customer.
        Assert.Equal(1, _billing.CreatedSubscriptionCount);
        Assert.Equal(1, _billing.CreatedCustomerCount);

        // ...and must not announce a second activation.
        Assert.Single(_publisher.Published.OfType<SubscriptionActivated>());
    }

    [Fact]
    public async Task SubscribeAsync_EnrolsAgainOnceThePreviousSubscriptionIsNoLongerLive()
    {
        _billing.SeedSubscription(Owner, SubscriptionState.Canceled);

        await Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "eshop-pro");

        Assert.Equal(1, _billing.CreatedSubscriptionCount);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlanBeforeTouchingTheCustomer()
    {
        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "ghost-plan"));

        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("EnsureCustomerAsync"));
        Assert.Equal(0, _billing.CreatedSubscriptionCount);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnArchivedPlan()
    {
        _billing.Plans.Add(new BillingPlan(9, "retired", "Retired", null, 100, 1, "month", false, DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "retired"));
    }

    [Fact]
    public async Task SubscribeAsync_KeepsTheSubscriptionWhenAnnouncingItFails()
    {
        _publisher.Failure = new InvalidOperationException("a handler blew up");

        var subscription = await Service.SubscribeAsync(SubscriptionActor.Customer(Owner), "eshop-pro");

        // Eventing is best-effort: the completed enrollment stands and the failure is logged.
        Assert.Equal(1, _billing.CreatedSubscriptionCount);
        Assert.NotEqual(0, subscription.Id);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("SubscriptionActivated"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsNothingForAUserWithNoBillingRecord()
    {
        Assert.Empty(await Service.GetSubscriptionsAsync("nobody@microsoft.com"));
    }

    // ---------- Ownership ----------

    [Fact]
    public async Task GetSubscriptionAsync_HidesASubscriptionBelongingToAnotherCustomer()
    {
        var owned = _billing.SeedSubscription(Owner);

        var seen = await Service.GetSubscriptionAsync(SubscriptionActor.Customer(Stranger), owned.Id);

        // Indistinguishable from "does not exist", so it cannot be probed for.
        Assert.Null(seen);
    }

    [Fact]
    public async Task GetSubscriptionAsync_LetsAnAdministratorSeeAnyCustomersSubscription()
    {
        var owned = _billing.SeedSubscription(Owner);

        var seen = await Service.GetSubscriptionAsync(SubscriptionActor.Administrator(), owned.Id);

        Assert.NotNull(seen);
        Assert.Equal(owned.Id, seen!.Id);
    }

    [Fact]
    public async Task ALifecycleActionOnAnotherCustomersSubscriptionIsRefused()
    {
        var owned = _billing.SeedSubscription(Owner);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => Service.ApplyLifecycleActionAsync(
                SubscriptionActor.Customer(Stranger), owned.Id, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.Immediate, null));

        Assert.Equal(404, exception.StatusCode);
        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("CancelSubscriptionAsync"));
    }

    [Fact]
    public async Task RecordingUsageOnAnotherCustomersSubscriptionIsRefused()
    {
        var owned = _billing.SeedSubscription(Owner);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => Service.RecordUsageAsync(SubscriptionActor.Customer(Stranger), owned.Id, 1m, null));

        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("RecordUsageAsync"));
    }

    // ---------- UC2: usage ----------

    [Fact]
    public async Task RecordUsageAsync_RecordsTheUnitsAndReturnsTheRunningTotal()
    {
        var subscription = _billing.SeedSubscription(Owner);

        var report = await Service.RecordUsageAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, 5m, "five calls");

        Assert.Equal(5m, report.Record.Quantity);
        Assert.Equal(42m, report.PeriodToDateQuantity);
        Assert.Equal(1L, report.UnitPriceInCents);

        // 42 units at $0.01 is $0.42.
        Assert.Equal(0.42m, report.PeriodToDateCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantityBeforeAnyProviderCall(decimal quantity)
    {
        var subscription = _billing.SeedSubscription(Owner);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.RecordUsageAsync(SubscriptionActor.Customer(Owner), subscription.Id, quantity, null));

        Assert.Empty(_billing.Calls);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesWhenTheConfiguredComponentIsNotMetered()
    {
        var subscription = _billing.SeedSubscription(Owner);
        _billing.ComponentConfigurationFailure = new BillingConfigurationException("not metered");

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.RecordUsageAsync(SubscriptionActor.Customer(Owner), subscription.Id, 1m, null));

        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesWhenTheSubscriptionIsNotLive()
    {
        var subscription = _billing.SeedSubscription(Owner, SubscriptionState.Canceled);

        await Assert.ThrowsAsync<SubscriptionStateException>(
            () => Service.RecordUsageAsync(SubscriptionActor.Customer(Owner), subscription.Id, 1m, null));

        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_KeepsTheUsageWhenTheRunningTotalCannotBeRead()
    {
        var subscription = _billing.SeedSubscription(Owner);
        _billing.PeriodToDateFailure = new BillingProviderException("read-back failed");

        var report = await Service.RecordUsageAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, 3m, null);

        // The units are already billed; only the total is unavailable.
        Assert.Equal(3m, report.Record.Quantity);
        Assert.False(report.PeriodToDateAvailable);
        Assert.Null(report.PeriodToDateCharge);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_BillsTheUsersLiveSubscription()
    {
        var subscription = _billing.SeedSubscription(Owner);

        var report = await Service.RecordUsageForUserAsync(Owner, 1m, "one order");

        Assert.NotNull(report);
        Assert.Equal(subscription.Id, report!.Record.SubscriptionId);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_DoesNothingWhenTheUserHasNoBillingRecord()
    {
        Assert.Null(await Service.RecordUsageForUserAsync("nobody@microsoft.com", 1m, null));
    }

    [Fact]
    public async Task RecordUsageForUserAsync_DoesNothingWhenTheUserHasNoLiveSubscription()
    {
        _billing.SeedSubscription(Owner, SubscriptionState.Canceled);

        // An ordinary outcome for a shopper who never subscribed — not an error.
        Assert.Null(await Service.RecordUsageForUserAsync(Owner, 1m, null));
    }

    // ---------- UC3: plan change ----------

    [Fact]
    public async Task ChangePlanAsync_MovesThePlanAndAnnouncesTheChange()
    {
        var subscription = _billing.SeedSubscription(Owner, planHandle: "eshop-pro");

        var updated = await Service.ChangePlanAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, "basic-plan",
            PlanChangeTiming.Immediate, expectedPaymentDueInCents: null);

        Assert.Equal("basic-plan", updated.PlanHandle);

        var changed = _publisher.Single<SubscriptionPlanChanged>();
        Assert.Equal("eshop-pro", changed.PreviousPlanHandle);
        Assert.Equal("basic-plan", changed.Subscription.PlanHandle);
    }

    [Fact]
    public async Task ChangePlanAsync_RefusesWhenThePreviewedAmountNoLongerHolds()
    {
        var subscription = _billing.SeedSubscription(Owner, planHandle: "eshop-pro");
        _billing.PreviewPaymentDueInCents = 20000;

        var exception = await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => Service.ChangePlanAsync(
                SubscriptionActor.Customer(Owner), subscription.Id, "basic-plan",
                PlanChangeTiming.Immediate, expectedPaymentDueInCents: 16400));

        Assert.Equal(16400, exception.ExpectedPaymentDueInCents);
        Assert.Equal(20000, exception.ActualPaymentDueInCents);

        // The customer is never charged an amount they did not confirm.
        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("ChangePlanAsync"));
        Assert.Empty(_publisher.Published.OfType<SubscriptionPlanChanged>());
    }

    [Fact]
    public async Task ChangePlanAsync_ProceedsWhenThePreviewedAmountStillHolds()
    {
        var subscription = _billing.SeedSubscription(Owner, planHandle: "eshop-pro");
        _billing.PreviewPaymentDueInCents = 16400;

        var updated = await Service.ChangePlanAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, "basic-plan",
            PlanChangeTiming.Immediate, expectedPaymentDueInCents: 16400);

        Assert.Equal("basic-plan", updated.PlanHandle);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var subscription = _billing.SeedSubscription(Owner, planHandle: "eshop-pro");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service.ChangePlanAsync(
                SubscriptionActor.Customer(Owner), subscription.Id, "eshop-pro",
                PlanChangeTiming.Immediate, null));

        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("ChangePlanAsync"));
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAChangeOnASubscriptionThatIsNotLive()
    {
        var subscription = _billing.SeedSubscription(Owner, SubscriptionState.Canceled, "eshop-pro");

        var exception = await Assert.ThrowsAsync<SubscriptionStateException>(
            () => Service.ChangePlanAsync(
                SubscriptionActor.Customer(Owner), subscription.Id, "basic-plan",
                PlanChangeTiming.Immediate, null));

        Assert.Equal(SubscriptionState.Canceled, exception.CurrentState);
        Assert.Contains(SubscriptionLifecycleAction.Reactivate, exception.AllowedTransitions);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_ReturnsTheProviderQuote()
    {
        var subscription = _billing.SeedSubscription(Owner, planHandle: "eshop-pro");

        var preview = await Service.PreviewPlanChangeAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(16400, preview.PaymentDueInCents);
        Assert.Equal(164.00m, preview.PaymentDue);
    }

    // ---------- UC4: lifecycle ----------

    [Fact]
    public async Task ApplyLifecycleActionAsync_AppliesTheTransitionAndAnnouncesOldToNewState()
    {
        var subscription = _billing.SeedSubscription(Owner);

        var updated = await Service.ApplyLifecycleActionAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, SubscriptionLifecycleAction.Pause,
            CancellationTiming.Immediate, null);

        Assert.Equal(SubscriptionState.Paused, updated.State);

        var changed = _publisher.Single<SubscriptionStateChanged>();
        Assert.Equal(SubscriptionState.Active, changed.PreviousState);
        Assert.Equal(SubscriptionState.Paused, changed.NewState);
        Assert.Equal(SubscriptionLifecycleAction.Pause, changed.Action);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_RefusesAnIllegalTransitionWithoutCallingTheProvider()
    {
        var subscription = _billing.SeedSubscription(Owner);

        var exception = await Assert.ThrowsAsync<SubscriptionStateException>(
            () => Service.ApplyLifecycleActionAsync(
                SubscriptionActor.Customer(Owner), subscription.Id, SubscriptionLifecycleAction.Resume,
                CancellationTiming.Immediate, null));

        Assert.Equal(SubscriptionState.Active, exception.CurrentState);
        Assert.Contains(SubscriptionLifecycleAction.Pause, exception.AllowedTransitions);
        Assert.DoesNotContain(_billing.Calls, call => call.StartsWith("ResumeSubscriptionAsync"));
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_PassesTheCancellationTimingAndReasonThrough()
    {
        var subscription = _billing.SeedSubscription(Owner);

        var updated = await Service.ApplyLifecycleActionAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, SubscriptionLifecycleAction.Cancel,
            CancellationTiming.EndOfPeriod, "too expensive");

        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.Contains("CancelSubscriptionAsync:EndOfPeriod:too expensive", _billing.Calls);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_KeepsTheTransitionWhenAnnouncingItFails()
    {
        var subscription = _billing.SeedSubscription(Owner);
        _publisher.Failure = new InvalidOperationException("a handler blew up");

        var updated = await Service.ApplyLifecycleActionAsync(
            SubscriptionActor.Customer(Owner), subscription.Id, SubscriptionLifecycleAction.Pause,
            CancellationTiming.Immediate, null);

        Assert.Equal(SubscriptionState.Paused, updated.State);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("SubscriptionStateChanged"));
    }

    [Fact]
    public async Task AnUnknownSubscriptionIsReportedAsNotFound()
    {
        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => Service.ApplyLifecycleActionAsync(
                SubscriptionActor.Customer(Owner), 123456, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.Immediate, null));

        Assert.Equal(404, exception.StatusCode);
    }
}
