using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class SubscriptionServiceTests
{
    private readonly IBillingClient _billing = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();

    // ---------- UC1: subscribe ----------

    [Fact]
    public async Task GetPlansAsync_hides_archived_plans_and_orders_by_price()
    {
        _billing.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionFakes.Pro(), SubscriptionFakes.Archived(), SubscriptionFakes.Basic() });

        var plans = (await SubscriptionFakes.Service(_billing).GetPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);
        Assert.Equal(MaxioTestContext.BASIC_HANDLE, plans[0].Handle);
        Assert.Equal(MaxioTestContext.PRO_HANDLE, plans[1].Handle);
    }

    [Fact]
    public async Task SubscribeAsync_creates_the_provider_customer_when_the_user_has_none_yet()
    {
        _billing.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Pro());
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _billing.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billing.CreateSubscriptionAsync(SubscriptionFakes.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());

        var subscription = await SubscriptionFakes.Service(_billing, _publisher)
            .SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        Assert.Equal(SubscriptionFakes.SUBSCRIPTION_ID, subscription.Id);

        // The user's email is the stable reference that makes a repeated subscribe idempotent.
        await _billing.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == SubscriptionFakes.USER && c.Email == SubscriptionFakes.USER),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_reuses_an_existing_provider_customer()
    {
        ArrangeSubscribable();

        await SubscriptionFakes.Service(_billing, _publisher).SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        await _billing.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_returns_the_existing_active_subscription_instead_of_enrolling_twice()
    {
        var existing = SubscriptionFakes.Subscription();
        _billing.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Pro());
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var subscription = await SubscriptionFakes.Service(_billing, _publisher)
            .SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        Assert.Same(existing, subscription);
        await _billing.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_enrolls_when_the_only_prior_subscription_is_cancelled()
    {
        _billing.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Pro());
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionFakes.Subscription(SubscriptionStatus.Canceled) });
        _billing.CreateSubscriptionAsync(SubscriptionFakes.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());

        await SubscriptionFakes.Service(_billing, _publisher).SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        await _billing.Received(1).CreateSubscriptionAsync(SubscriptionFakes.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_refuses_an_unresolvable_plan_without_touching_the_customer_record()
    {
        _billing.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => SubscriptionFakes.Service(_billing).SubscribeAsync(SubscriptionFakes.USER, "ghost-plan"));

        await _billing.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_publishes_the_activation_notification()
    {
        ArrangeSubscribable();

        await SubscriptionFakes.Service(_billing, _publisher).SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.UserReference == SubscriptionFakes.USER
                && n.Subscription.Id == SubscriptionFakes.SUBSCRIPTION_ID),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_still_succeeds_when_an_in_process_handler_fails()
    {
        ArrangeSubscribable();
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("handler blew up"));

        // Eventing is best effort: a failing handler must never undo a completed enrollment.
        var subscription = await SubscriptionFakes.Service(_billing, _publisher)
            .SubscribeAsync(SubscriptionFakes.USER, MaxioTestContext.PRO_HANDLE);

        Assert.Equal(SubscriptionFakes.SUBSCRIPTION_ID, subscription.Id);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_for_a_user_the_provider_has_never_seen()
    {
        _billing.FindCustomerByReferenceAsync("stranger@example.com", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var subscriptions = await SubscriptionFakes.Service(_billing).GetSubscriptionsAsync("stranger@example.com");

        Assert.Empty(subscriptions);
        await _billing.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------- UC2: usage ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RecordUsageAsync_rejects_a_non_positive_quantity_before_calling_the_provider(decimal quantity)
    {
        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, quantity, null, null));

        await _billing.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_refuses_when_the_configured_component_is_not_metered()
    {
        _billing.GetMeteredComponentAsync(Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Component(isMetered: false));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 1m, null, null));

        Assert.Contains("not of metered kind", exception.Message);
        await _billing.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_refuses_when_the_configured_component_does_not_resolve()
    {
        _billing.GetMeteredComponentAsync(Arg.Any<CancellationToken>()).Returns((MeteredComponentDefinition?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 1m, null, null));
    }

    [Fact]
    public async Task RecordUsageAsync_refuses_a_subscription_that_is_not_active()
    {
        ArrangeMeteredComponent();
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 1m, null, null));

        Assert.Contains("Canceled", exception.Message);
        await _billing.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_reports_the_period_total_and_what_it_will_cost()
    {
        ArrangeUsageRecordable(periodToDate: 250m);

        var report = await SubscriptionFakes.Service(_billing)
            .RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 5m, "order 1001", SubscriptionFakes.USER);

        Assert.True(report.TotalsAvailable);
        Assert.Equal(250m, report.PeriodToDateQuantity);
        Assert.Equal(17, report.CurrentUnitBalance);
        Assert.Equal(0.01m, report.UnitPrice);
        // 250 units at one cent each is $2.50 — not $250.00 and not $0.03.
        Assert.Equal(2.50m, report.PeriodToDateCharge);
    }

    [Fact]
    public async Task RecordUsageAsync_keeps_the_recorded_usage_when_the_total_cannot_be_read_back()
    {
        ArrangeMeteredComponent();
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionFakes.COMPONENT_ID, 5m, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.UsageRecord(5m));
        _billing.GetComponentUnitBalanceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("ReadSubscriptionComponent", "upstream timeout"));

        var report = await SubscriptionFakes.Service(_billing)
            .RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 5m, null, null);

        // The units are already billed; a failed read-back must not fail the operation or invite a resend.
        Assert.False(report.TotalsAvailable);
        Assert.Null(report.PeriodToDateQuantity);
        Assert.Null(report.PeriodToDateCharge);
        Assert.Equal(5m, report.Record.Quantity);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_meters_the_users_active_subscription()
    {
        ArrangeUsageRecordable(periodToDate: 1m);
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionFakes.Subscription(SubscriptionStatus.Canceled, id: 41), SubscriptionFakes.Subscription() });

        var report = await SubscriptionFakes.Service(_billing).RecordUsageForUserAsync(SubscriptionFakes.USER, 5m, "order 1001");

        Assert.Equal(SubscriptionFakes.SUBSCRIPTION_ID, report.SubscriptionId);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_refuses_a_user_with_no_active_subscription()
    {
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionFakes.Subscription(SubscriptionStatus.Canceled) });

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageForUserAsync(SubscriptionFakes.USER, 1m, null));

        await _billing.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUsageSummaryAsync_reports_the_running_total_and_estimated_charge()
    {
        ArrangeMeteredComponent();
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.GetComponentUnitBalanceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(17);
        _billing.SumUsageSinceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(103m);

        var summary = await SubscriptionFakes.Service(_billing)
            .GetUsageSummaryAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionFakes.USER);

        Assert.Equal(103m, summary.PeriodToDateQuantity);
        Assert.Equal(1.03m, summary.EstimatedCharge);
        Assert.Equal("call", summary.UnitName);
    }

    // ---------- Ownership ----------

    [Fact]
    public async Task A_customer_cannot_report_usage_against_someone_elses_subscription()
    {
        ArrangeMeteredComponent();
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(customerReference: SubscriptionFakes.OTHER_USER));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, 1m, null, SubscriptionFakes.USER));

        await _billing.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_customer_cannot_change_the_lifecycle_of_someone_elses_subscription()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(customerReference: SubscriptionFakes.OTHER_USER));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).ApplyLifecycleActionAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.CancelImmediately, null, SubscriptionFakes.USER));

        await _billing.DidNotReceive().CancelSubscriptionImmediatelyAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_administrator_may_act_on_any_subscription()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(customerReference: SubscriptionFakes.OTHER_USER));
        _billing.CancelSubscriptionImmediatelyAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Canceled, customerReference: SubscriptionFakes.OTHER_USER));

        // A null owner reference means "no ownership restriction" — the administrator path.
        var updated = await SubscriptionFakes.Service(_billing).ApplyLifecycleActionAsync(
            SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.CancelImmediately, null, null);

        Assert.Equal(SubscriptionStatus.Canceled, updated.Status);
    }

    [Fact]
    public async Task An_unknown_subscription_id_is_rejected_before_any_write()
    {
        _billing.GetSubscriptionAsync(999999, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).ApplyLifecycleActionAsync(
                999999, SubscriptionLifecycleAction.Pause, null, null));

        Assert.Contains("999999", exception.Message);
    }

    // ---------- UC3: plan change ----------

    [Fact]
    public async Task PreviewPlanChangeAsync_rejects_a_move_to_the_plan_the_subscription_is_already_on()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).PreviewPlanChangeAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.PRO_HANDLE, PlanChangeTiming.Immediately, null));

        await _billing.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_rejects_a_change_on_a_cancelled_subscription()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).PreviewPlanChangeAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, PlanChangeTiming.Immediately, null));

        Assert.Contains("Reactivate", exception.Message);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_at_next_renewal_quotes_nothing_due_and_never_asks_for_a_proration()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.FindPlanByHandleAsync(MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Basic());

        var preview = await SubscriptionFakes.Service(_billing).PreviewPlanChangeAsync(
            SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, PlanChangeTiming.AtNextRenewal, null);

        Assert.Equal(0m, preview.AmountDue);
        Assert.Equal(0m, preview.ProratedCharge);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), preview.EffectiveAt);
        await _billing.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_refuses_an_archived_target_plan()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.FindPlanByHandleAsync("retired-plan", Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Archived());

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => SubscriptionFakes.Service(_billing).PreviewPlanChangeAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, "retired-plan", PlanChangeTiming.Immediately, null));
    }

    [Fact]
    public async Task ChangePlanAsync_commits_and_publishes_when_the_quote_still_matches()
    {
        ArrangePlanChangeable(amountDue: -135.50m);
        _billing.ChangePlanImmediatelyAsync(SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(planHandle: MaxioTestContext.BASIC_HANDLE, planPrice: 29.00m));

        var result = await SubscriptionFakes.Service(_billing, _publisher).ChangePlanAsync(
            SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, PlanChangeTiming.Immediately, -135.50m, null);

        Assert.Equal(MaxioTestContext.PRO_HANDLE, result.PreviousPlanHandle);
        Assert.Equal(MaxioTestContext.BASIC_HANDLE, result.TargetPlanHandle);
        Assert.Equal(-135.50m, result.AmountApplied);
        Assert.Equal(29.00m, result.Subscription.PlanPrice);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n => n.TargetPlanHandle == MaxioTestContext.BASIC_HANDLE),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlanAsync_refuses_to_charge_an_amount_other_than_the_one_the_customer_confirmed()
    {
        // The customer was quoted -135.50, but the provider now quotes -100.00.
        ArrangePlanChangeable(amountDue: -100.00m);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing, _publisher).ChangePlanAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, PlanChangeTiming.Immediately, -135.50m, null));

        Assert.Contains("no longer current", exception.Message);
        await _billing.DidNotReceive().ChangePlanImmediatelyAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlanAsync_at_next_renewal_schedules_the_change_rather_than_prorating_it()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.FindPlanByHandleAsync(MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Basic());
        _billing.ChangePlanAtRenewalAsync(SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());

        var result = await SubscriptionFakes.Service(_billing, _publisher).ChangePlanAsync(
            SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, PlanChangeTiming.AtNextRenewal, 0m, null);

        Assert.Equal(0m, result.AmountApplied);
        await _billing.Received(1).ChangePlanAtRenewalAsync(SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ChangePlanImmediatelyAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- UC4: lifecycle ----------

    [Theory]
    [InlineData(SubscriptionStatus.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionStatus.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionStatus.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionStatus.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionStatus.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionStatus.PastDue, SubscriptionLifecycleAction.CancelAtPeriodEnd)]
    [InlineData(SubscriptionStatus.Expired, SubscriptionLifecycleAction.CancelImmediately)]
    public async Task Illegal_transitions_are_refused_without_calling_the_provider(SubscriptionStatus status, SubscriptionLifecycleAction action)
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(status));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => SubscriptionFakes.Service(_billing).ApplyLifecycleActionAsync(SubscriptionFakes.SUBSCRIPTION_ID, action, null, null));

        Assert.Contains(status.ToString(), exception.Message);
        Assert.Contains("Legal transitions", exception.Message);

        await _billing.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CancelSubscriptionImmediatelyAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CancelSubscriptionAtPeriodEndAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pausing_an_active_subscription_publishes_the_old_and_new_state()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.PauseSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Paused));

        var updated = await SubscriptionFakes.Service(_billing, _publisher)
            .ApplyLifecycleActionAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.Pause, null, SubscriptionFakes.USER);

        Assert.Equal(SubscriptionStatus.Paused, updated.Status);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.PreviousStatus == SubscriptionStatus.Active
                && n.NewStatus == SubscriptionStatus.Paused
                && n.Action == SubscriptionLifecycleAction.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resuming_a_paused_subscription_is_allowed()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Paused));
        _billing.ResumeSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());

        var updated = await SubscriptionFakes.Service(_billing)
            .ApplyLifecycleActionAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.Resume, null, null);

        Assert.Equal(SubscriptionStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Reactivating_a_cancelled_subscription_is_allowed()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(SubscriptionStatus.Canceled));
        _billing.ReactivateSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());

        var updated = await SubscriptionFakes.Service(_billing)
            .ApplyLifecycleActionAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.Reactivate, null, null);

        Assert.Equal(SubscriptionStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Cancelling_at_period_end_passes_the_reason_through_to_the_provider()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.CancelSubscriptionAtPeriodEndAsync(SubscriptionFakes.SUBSCRIPTION_ID, "Too expensive", Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription(cancelAtEndOfPeriod: true));

        var updated = await SubscriptionFakes.Service(_billing).ApplyLifecycleActionAsync(
            SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.CancelAtPeriodEnd, "Too expensive", null);

        Assert.True(updated.IsPendingCancellation);
        await _billing.Received(1).CancelSubscriptionAtPeriodEndAsync(SubscriptionFakes.SUBSCRIPTION_ID, "Too expensive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_provider_failure_during_a_transition_reaches_the_caller_as_a_typed_exception()
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.PauseSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("PauseSubscription", "state drifted", 422));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => SubscriptionFakes.Service(_billing, _publisher).ApplyLifecycleActionAsync(
                SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionLifecycleAction.Pause, null, null));

        Assert.Equal(422, exception.StatusCode);
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    // ---------- arrangement helpers ----------

    private void ArrangeSubscribable()
    {
        _billing.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Pro());
        _billing.FindCustomerByReferenceAsync(SubscriptionFakes.USER, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Customer());
        _billing.ListSubscriptionsForCustomerAsync(SubscriptionFakes.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billing.CreateSubscriptionAsync(SubscriptionFakes.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Subscription());
    }

    private void ArrangeMeteredComponent()
    {
        _billing.GetMeteredComponentAsync(Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Component());
    }

    private void ArrangeUsageRecordable(decimal periodToDate)
    {
        ArrangeMeteredComponent();
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.RecordUsageAsync(SubscriptionFakes.SUBSCRIPTION_ID, SubscriptionFakes.COMPONENT_ID, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => SubscriptionFakes.UsageRecord(callInfo.ArgAt<decimal>(2)));
        _billing.GetComponentUnitBalanceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(17);
        _billing.SumUsageSinceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(periodToDate);
    }

    private void ArrangePlanChangeable(decimal amountDue)
    {
        _billing.GetSubscriptionAsync(SubscriptionFakes.SUBSCRIPTION_ID, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Subscription());
        _billing.FindPlanByHandleAsync(MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>()).Returns(SubscriptionFakes.Basic());
        _billing.PreviewPlanChangeAsync(SubscriptionFakes.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionFakes.Preview(amountDue));
    }
}
