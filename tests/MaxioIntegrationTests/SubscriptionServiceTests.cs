using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The orchestration over the provider-agnostic seam: what is rejected before any provider call,
/// what is idempotent, and which in-process notifications are published.
/// </summary>
public class SubscriptionServiceTests
{
    private const string UserName = "demouser@microsoft.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly RecordingAppLogger<SubscriptionService> _logger = new();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _service = new SubscriptionService(_billingClient, _publisher, _logger);

        _billingClient.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(MaxioPayloads.CustomerId, UserName, UserName, "demouser", "eShopOnWeb"));
        _billingClient.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProPlan);
        _billingClient.GetPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>()).Returns(BasicPlan);
        _billingClient.ListSubscriptionsForCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _billingClient.GetMeteredComponentAsync(Arg.Any<CancellationToken>()).Returns(MeteredComponent);
    }

    // --- UC1: subscribe ---------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_EnrollsTheUserAndPublishesSubscriptionActivated()
    {
        var created = SubscriptionOn(ProPlan, SubscriptionState.Active, "active");
        _billingClient.CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(created);

        var subscription = await _service.SubscribeAsync(UserName, "eshop-pro");

        Assert.Same(created, subscription);
        await _billingClient.Received(1).CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro", Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.UserName == UserName && n.Subscription == created),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscription_WhenOneIsAlreadyLive()
    {
        var existing = SubscriptionOn(ProPlan, SubscriptionState.Active, "active");
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var subscription = await _service.SubscribeAsync(UserName, "eshop-pro");

        Assert.Same(existing, subscription);
        // A double-click must never create a second enrollment, and nothing new happened to announce.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_EnrollsAgain_WhenTheOnlyExistingSubscriptionIsCancelled()
    {
        var cancelled = SubscriptionOn(ProPlan, SubscriptionState.Cancelled, "canceled");
        var created = SubscriptionOn(ProPlan, SubscriptionState.Active, "active");
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { cancelled });
        _billingClient.CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(created);

        Assert.Same(created, await _service.SubscribeAsync(UserName, "eshop-pro"));
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenThePlanHandleDoesNotResolve_AndNeverCreatesACustomer()
    {
        _billingClient.GetPlanByHandleAsync("stale-handle", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(UserName, "stale-handle"));

        Assert.Contains("stale-handle", exception.Message);
        await _billingClient.DidNotReceive().EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_KeepsTheSubscription_WhenPublishingTheNotificationFails()
    {
        var created = SubscriptionOn(ProPlan, SubscriptionState.Active, "active");
        _billingClient.CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(created);
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        // Eventing is best-effort: the enrollment stands and the failure is logged, not rethrown.
        Assert.Same(created, await _service.SubscribeAsync(UserName, "eshop-pro"));
        Assert.Contains(_logger.Warnings, w => w.Contains("SubscriptionActivated"));
    }

    [Fact]
    public async Task GetLiveSubscriptionForUserAsync_ReturnsNull_WhenTheUserHasOnlyEndedSubscriptions()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionOn(ProPlan, SubscriptionState.Expired, "expired") });

        Assert.Null(await _service.GetLiveSubscriptionForUserAsync(UserName));
    }

    // --- UC2: pay-as-you-go usage -----------------------------------------------------------

    [Fact]
    public async Task RecordUsageAsync_RecordsTheUsageAndReportsThePeriodToDateTotalAndItsCost()
    {
        GivenLiveSubscription();
        _billingClient.RecordUsageAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, 25m, "batch", Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, "api-call", 25m, "batch", DateTimeOffset.UtcNow));
        _billingClient.GetUsageTotalAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, Arg.Any<CancellationToken>())
            .Returns(35m);

        var report = await _service.RecordUsageAsync(UserName, 25m, "batch");

        Assert.Equal(25m, report.Record.Quantity);
        Assert.Equal(35m, report.PeriodToDateTotal);
        Assert.Equal(0.01m, report.UnitPrice);
        // 35 units at one cent each is 35 cents on the next renewal invoice.
        Assert.Equal(0.35m, report.EstimatedCharge);
    }

    [Fact]
    public async Task RecordUsageAsync_KeepsTheUsage_WhenTheRunningTotalCannotBeReadBack()
    {
        GivenLiveSubscription();
        _billingClient.RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, "api-call", 5m, null, DateTimeOffset.UtcNow));
        _billingClient.GetUsageTotalAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("read-back timed out"));

        var report = await _service.RecordUsageAsync(UserName, 5m, null);

        Assert.Equal(5m, report.Record.Quantity);
        Assert.Null(report.PeriodToDateTotal);
        Assert.Null(report.EstimatedCharge);
        Assert.Contains(_logger.Warnings, w => w.Contains("could not read back"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantity_BeforeAnyProviderCall(decimal quantity)
    {
        GivenLiveSubscription();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(UserName, quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_Rejects_WhenTheUserHasNoActiveSubscription()
    {
        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(UserName, 1m, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_Refuses_WhenTheConfiguredComponentIsNotOfMeteredKind()
    {
        GivenLiveSubscription();
        _billingClient.GetMeteredComponentAsync(Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(MaxioPayloads.ApiCallComponentId, "api-call", "API Calls", "quantity_based_component", "per_unit", 0.01m, 3026729));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.RecordUsageAsync(UserName, 1m, null));

        Assert.Contains("quantity_based_component", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageAsync_Refuses_WhenTheConfiguredComponentDoesNotResolveAtAll()
    {
        GivenLiveSubscription();
        _billingClient.GetMeteredComponentAsync(Arg.Any<CancellationToken>()).Returns((MeteredComponent?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _service.RecordUsageAsync(UserName, 1m, null));
    }

    // --- UC3: plan change -------------------------------------------------------------------

    [Fact]
    public async Task ChangePlanAsync_CommitsAndPublishesSubscriptionPlanChanged_CarryingTheOldPlan()
    {
        GivenLiveSubscription();
        GivenPreview(paymentDueInCents: 0);
        var changed = SubscriptionOn(BasicPlan, SubscriptionState.Active, "active");
        _billingClient.ChangePlanAsync(MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(changed);

        var subscription = await _service.ChangePlanAsync(UserName, "basic-plan", PlanChangeTiming.Immediate, 0);

        Assert.Same(changed, subscription);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n => n.PreviousPlan.Handle == "eshop-pro" && n.NewPlan.Handle == "basic-plan"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAStalePreview_AndNeverCommits()
    {
        GivenLiveSubscription();
        GivenPreview(paymentDueInCents: 2934);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(UserName, "basic-plan", PlanChangeTiming.Immediate, previewedPaymentDueInCents: 0));

        // The customer confirmed 0 cents; the provider now quotes 2934, so the change must not apply.
        Assert.Contains("2934", exception.Message);
        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlanAsync_CommitsWithoutAStalenessCheck_WhenNoPreviewedAmountIsSupplied()
    {
        GivenLiveSubscription();
        GivenPreview(paymentDueInCents: 2934);
        var changed = SubscriptionOn(BasicPlan, SubscriptionState.Active, "active");
        _billingClient.ChangePlanAsync(Arg.Any<int>(), "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(changed);

        Assert.Same(changed, await _service.ChangePlanAsync(UserName, "basic-plan", PlanChangeTiming.Immediate, null));
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_RejectsANoOp_BeforeAnyProviderCall()
    {
        GivenLiveSubscription();

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(UserName, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", exception.Message);
        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<Subscription>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Rejects_WhenTheSubscriptionIsCancelled()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionOn(ProPlan, SubscriptionState.Cancelled, "canceled") });

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(UserName, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Reactivate", exception.Message);
    }

    // --- UC4: lifecycle ---------------------------------------------------------------------

    [Fact]
    public async Task PauseAsync_TransitionsAndPublishesSubscriptionStateChanged_CarryingOldAndNewState()
    {
        GivenLiveSubscription();
        var paused = SubscriptionOn(ProPlan, SubscriptionState.Paused, "on_hold");
        _billingClient.PauseSubscriptionAsync(MaxioPayloads.SubscriptionId, Arg.Any<CancellationToken>()).Returns(paused);

        var subscription = await _service.PauseAsync(UserName);

        Assert.Same(paused, subscription);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.PreviousState == SubscriptionState.Active && n.NewState == SubscriptionState.Paused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_RejectsAnActiveSubscription_BeforeAnyProviderCall()
    {
        GivenLiveSubscription();

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() => _service.ResumeAsync(UserName));

        Assert.Contains("active", exception.Message);
        Assert.Contains("paused", exception.Message);
        await _billingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ResumesAPausedSubscription()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionOn(ProPlan, SubscriptionState.Paused, "on_hold") });
        var resumed = SubscriptionOn(ProPlan, SubscriptionState.Active, "active");
        _billingClient.ResumeSubscriptionAsync(MaxioPayloads.SubscriptionId, Arg.Any<CancellationToken>()).Returns(resumed);

        Assert.Same(resumed, await _service.ResumeAsync(UserName));
    }

    [Fact]
    public async Task ReactivateAsync_RejectsAnActiveSubscription_BeforeAnyProviderCall()
    {
        GivenLiveSubscription();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() => _service.ReactivateAsync(UserName));
        await _billingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_PassesTheRequestedTimingAndReasonThrough()
    {
        GivenLiveSubscription();
        var cancelled = SubscriptionOn(ProPlan, SubscriptionState.Cancelled, "canceled");
        _billingClient.CancelSubscriptionAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, "too expensive", Arg.Any<CancellationToken>())
            .Returns(cancelled);

        var subscription = await _service.CancelAsync(UserName, CancellationTiming.EndOfPeriod, "too expensive");

        Assert.Same(cancelled, subscription);
        await _billingClient.Received(1).CancelSubscriptionAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, "too expensive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_Rejects_WhenTheUserHasNoSubscriptionAtAll()
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.CancelAsync(UserName, CancellationTiming.Immediate, null));

        Assert.Contains("no subscription", exception.Message);
    }

    [Fact]
    public async Task ALifecycleTransition_LetsAProviderFailureSurface_RatherThanReportingSuccess()
    {
        GivenLiveSubscription();
        _billingClient.PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("Subscription is not currently active.", 422, new[] { "Subscription is not currently active." }));

        await Assert.ThrowsAsync<BillingProviderException>(() => _service.PauseAsync(UserName));
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    // --- helpers ----------------------------------------------------------------------------

    private static SubscriptionPlan ProPlan => new(MaxioPayloads.ProPlanId, "eshop-pro", "Pro Plan", null, 29900, 1, "month");

    private static SubscriptionPlan BasicPlan => new(MaxioPayloads.BasicPlanId, "basic-plan", "Basic Plan", null, 2900, 1, "month");

    private static MeteredComponent MeteredComponent =>
        new(MaxioPayloads.ApiCallComponentId, "api-call", "API Calls", MeteredComponent.MeteredKind, "per_unit", 0.01m, 3026729);

    private static Subscription SubscriptionOn(SubscriptionPlan plan, SubscriptionState state, string providerState)
    {
        return new Subscription(MaxioPayloads.SubscriptionId,
            MaxioPayloads.CustomerId,
            UserName,
            plan,
            state,
            providerState,
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow.AddDays(30),
            cancelAtEndOfPeriod: false,
            delayedCancelAt: null,
            balanceInCents: plan.PriceInCents);
    }

    private void GivenLiveSubscription()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionOn(ProPlan, SubscriptionState.Active, "active") });
    }

    private void GivenPreview(int paymentDueInCents)
    {
        _billingClient.PreviewPlanChangeAsync(Arg.Any<Subscription>(), "basic-plan", Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview(ProPlan, BasicPlan, PlanChangeTiming.Immediate, -29900, 2934, paymentDueInCents, -26966));
    }
}
