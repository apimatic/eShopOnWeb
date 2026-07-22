using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The rules the subscription service enforces over the provider seam — the ones that decide whether a
/// provider call happens at all, and what is announced in-process once it has.
/// </summary>
public class SubscriptionOrchestrationTests
{
    private const string UserName = "demouser@microsoft.com";

    private static (SubscriptionService Service, FakeBillingClient Billing, RecordingPublisher Publisher) Build()
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(new SubscriptionPlan(7130995, "eshop-pro", "Pro Plan", 299.00m, 1, "month"));
        billing.Plans.Add(new SubscriptionPlan(7130996, "basic-plan", "Basic Plan", 29.00m, 1, "month"));

        var publisher = new RecordingPublisher();
        var service = new SubscriptionService(billing, publisher, new NullAppLogger<SubscriptionService>());

        return (service, billing, publisher);
    }

    private static CustomerSubscription Existing(int id,
        SubscriptionLifecycleState state = SubscriptionLifecycleState.Active,
        string planHandle = "eshop-pro",
        bool cancelAtEndOfPeriod = false) =>
        new(id, state)
        {
            PlanHandle = planHandle,
            PlanName = planHandle,
            PlanPrice = planHandle == "eshop-pro" ? 299.00m : 29.00m,
            CustomerReference = UserName,
            CancelAtEndOfPeriod = cancelAtEndOfPeriod,
            NextAssessmentAt = DateTimeOffset.UtcNow.AddDays(20)
        };

    // ---------------- UC1 ----------------

    [Fact]
    public async Task Subscribing_creates_the_customer_then_the_subscription_and_announces_it()
    {
        var (service, billing, publisher) = Build();

        var subscription = await service.SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Contains(billing.Calls, c => c.StartsWith("EnsureCustomerAsync:" + UserName, StringComparison.Ordinal));
        Assert.Contains("CreateSubscriptionAsync:eshop-pro", billing.Calls);

        var activated = Assert.IsType<SubscriptionActivated>(Assert.Single(publisher.Published));
        Assert.Equal(UserName, activated.UserName);
        Assert.Equal("eshop-pro", activated.PlanHandle);
        Assert.Equal(299.00m, activated.PlanPrice);
    }

    [Fact]
    public async Task Subscribing_twice_returns_the_live_subscription_instead_of_enrolling_again()
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001));

        var subscription = await service.SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(1001, subscription.Id);
        // The critical guarantee: a double click must never produce a second billed enrolment.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateSubscriptionAsync", StringComparison.Ordinal));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Subscribing_to_an_unknown_plan_never_reaches_enrolment()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAsync<BillingConfigurationException>(() => service.SubscribeAsync(UserName, "ghost-plan"));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateSubscriptionAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_enrolment_stands_even_if_the_in_process_notification_fails()
    {
        var (service, billing, publisher) = Build();
        publisher.Throws = true;

        var subscription = await service.SubscribeAsync(UserName, "eshop-pro");

        // Best-effort eventing: the subscription was created, so it must not be rolled back.
        Assert.Equal(2001, subscription.Id);
        Assert.Contains("CreateSubscriptionAsync:eshop-pro", billing.Calls);
    }

    [Fact]
    public async Task The_billing_customer_name_is_derived_from_the_eshop_username()
    {
        var (service, billing, _) = Build();

        await service.SubscribeAsync("jane.roe@example.com", "eshop-pro");

        Assert.Contains("EnsureCustomerAsync:jane.roe@example.com:Jane Roe", billing.Calls);
    }

    // ---------------- UC2 ----------------

    [Fact]
    public async Task Recording_usage_returns_the_receipt_and_the_running_period_to_date_charge()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001));
        billing.PeriodToDateUnits = 250;

        var summary = await service.RecordUsageAsync(UserName, 5, "order 42");

        Assert.Equal(5m, summary.Receipt.Quantity);
        Assert.Equal(250, summary.PeriodToDateUnits);
        Assert.Equal(0.01m, summary.UnitPrice);
        // 250 units at one cent each.
        Assert.Equal(2.50m, summary.PeriodToDateCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_invalid_usage_quantity_is_rejected_before_anything_is_sent(int quantity)
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RecordUsageAsync(UserName, quantity, null));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_is_refused_when_the_customer_has_no_active_subscription()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => service.RecordUsageAsync(UserName, 1, null));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_is_refused_against_a_subscription_that_cannot_accrue_charges()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001, SubscriptionLifecycleState.Canceled));

        var exception = await Assert.ThrowsAsync<SubscriptionNotBillableException>(
            () => service.RecordUsageForSubscriptionAsync(1001, 1, null));

        Assert.Equal(SubscriptionLifecycleState.Canceled, exception.State);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_read_back_of_the_running_total_does_not_fail_the_usage_report()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001));
        billing.PeriodToDateFailure = new BillingProviderException(
            "GetPeriodToDateUsageAsync", "gateway timeout", 504);

        var summary = await service.RecordUsageAsync(UserName, 3, null);

        // The usage stands; only the running total is reported as unavailable.
        Assert.Equal(3m, summary.Receipt.Quantity);
        Assert.Null(summary.PeriodToDateUnits);
        Assert.Null(summary.PeriodToDateCharge);
    }

    // ---------------- UC3 ----------------

    [Fact]
    public async Task Changing_to_the_plan_the_subscription_is_already_on_is_refused_locally()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001, planHandle: "eshop-pro"));

        await Assert.ThrowsAsync<PlanChangeNotAllowedException>(
            () => service.PreviewPlanChangeAsync(UserName, 1001, "eshop-pro", PlanChangeTiming.Immediately));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("PreviewPlanChangeAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plan_change_is_refused_from_a_state_that_does_not_allow_one()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001, SubscriptionLifecycleState.Canceled));

        var exception = await Assert.ThrowsAsync<PlanChangeNotAllowedException>(
            () => service.ChangePlanAsync(UserName, 1001, "basic-plan", PlanChangeTiming.Immediately, null));

        Assert.Contains("reactivate", exception.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlanAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Committing_a_confirmed_preview_applies_it_and_announces_the_change()
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001, planHandle: "eshop-pro"));

        var preview = new PlanChangePreview(1001, "eshop-pro", "basic-plan", PlanChangeTiming.Immediately)
        {
            ProratedAdjustment = -134.50m,
            TargetPlanPrice = 29.00m
        };
        billing.NextPreview = preview;

        var result = await service.ChangePlanAsync(UserName, 1001, "basic-plan", PlanChangeTiming.Immediately, preview.Fingerprint);

        Assert.Equal("eshop-pro", result.PreviousPlanHandle);
        Assert.Equal("basic-plan", result.NewPlanHandle);
        Assert.Equal(-134.50m, result.ProrationAmount);

        var changed = Assert.IsType<SubscriptionPlanChanged>(Assert.Single(publisher.Published));
        Assert.Equal("basic-plan", changed.NewPlanHandle);
        Assert.Equal(-134.50m, changed.ProrationAmount);
    }

    [Fact]
    public async Task A_commit_is_refused_when_the_cost_moved_since_the_customer_confirmed_it()
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001, planHandle: "eshop-pro"));

        var shown = new PlanChangePreview(1001, "eshop-pro", "basic-plan", PlanChangeTiming.Immediately)
        {
            ProratedAdjustment = -134.50m,
            PaymentDue = 0m,
            TargetPlanPrice = 29.00m
        };

        billing.NextPreview = shown;
        // At commit time the provider now prices it differently.
        billing.RepricedPreview = shown with { PaymentDue = 87.65m };

        await service.PreviewPlanChangeAsync(UserName, 1001, "basic-plan", PlanChangeTiming.Immediately);

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => service.ChangePlanAsync(UserName, 1001, "basic-plan", PlanChangeTiming.Immediately, shown.Fingerprint));

        // Nothing was charged and nothing was announced.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlanAsync", StringComparison.Ordinal));
        Assert.Empty(publisher.Published);
    }

    // ---------------- UC4 ----------------

    [Theory]
    [InlineData(SubscriptionLifecycleState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionLifecycleState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionLifecycleState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionLifecycleState.Canceled, SubscriptionLifecycleAction.Resume)]
    public async Task An_illegal_transition_is_refused_without_calling_the_provider(
        SubscriptionLifecycleState from, SubscriptionLifecycleAction action)
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001, from));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.ApplyLifecycleActionAsync(UserName, 1001, action, CancellationTiming.EndOfPeriod, null));

        Assert.Equal(from, exception.CurrentState);
        Assert.Equal(action, exception.Action);
        Assert.NotNull(exception.LegalActions);
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData(SubscriptionLifecycleState.Active, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionLifecycleState.Paused, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionLifecycleState.Active, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionLifecycleState.Canceled, SubscriptionLifecycleAction.Reactivate)]
    public async Task A_legal_transition_is_applied_and_announced_with_old_and_new_state(
        SubscriptionLifecycleState from, SubscriptionLifecycleAction action)
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001, from));

        var updated = await service.ApplyLifecycleActionAsync(UserName, 1001, action, CancellationTiming.Immediate, "because");

        var changed = Assert.IsType<SubscriptionStateChanged>(Assert.Single(publisher.Published));
        Assert.Equal(from, changed.PreviousState);
        Assert.Equal(updated.State, changed.NewState);
        Assert.Equal(action, changed.Action);
    }

    [Fact]
    public async Task Reactivate_is_legal_while_a_cancellation_is_still_pending()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001, SubscriptionLifecycleState.Active, cancelAtEndOfPeriod: true));

        var updated = await service.ApplyLifecycleActionAsync(
            UserName, 1001, SubscriptionLifecycleAction.Reactivate, CancellationTiming.EndOfPeriod, null);

        Assert.Equal(SubscriptionLifecycleState.Active, updated.State);
        Assert.Contains("ReactivateSubscriptionAsync:1001", billing.Calls);
    }

    [Fact]
    public async Task An_end_of_period_cancellation_reports_the_date_it_takes_effect()
    {
        var (service, billing, publisher) = Build();
        billing.Subscriptions.Add(Existing(1001));

        var updated = await service.ApplyLifecycleActionAsync(
            UserName, 1001, SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod, "too expensive");

        Assert.True(updated.CancelAtEndOfPeriod);

        var changed = Assert.IsType<SubscriptionStateChanged>(Assert.Single(publisher.Published));
        Assert.NotNull(changed.EffectiveAt);
    }

    // ---------------- Ownership ----------------

    [Fact]
    public async Task A_customer_cannot_act_on_a_subscription_that_is_not_theirs()
    {
        var (service, billing, _) = Build();
        // The seam lists only this customer's subscriptions, so 9999 is somebody else's.
        billing.Subscriptions.Add(Existing(1001));

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.ApplyLifecycleActionAsync(UserName, 9999, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.Immediate, null));

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.GetPeriodToDateUsageAsync(UserName, 9999));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CancelSubscriptionAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_active_subscription_is_the_one_that_can_be_billed()
    {
        var (service, billing, _) = Build();
        billing.Subscriptions.Add(Existing(1001, SubscriptionLifecycleState.Canceled));
        billing.Subscriptions.Add(Existing(1002, SubscriptionLifecycleState.Active));

        var active = await service.GetActiveSubscriptionAsync(UserName);

        Assert.NotNull(active);
        Assert.Equal(1002, active!.Id);
    }
}
