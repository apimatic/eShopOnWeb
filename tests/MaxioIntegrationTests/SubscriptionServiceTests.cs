using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The provider-agnostic seam: the rules that must hold no matter which billing provider sits
/// behind <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionServiceTests
{
    private const string UserReference = "buyer@example.com";
    private const string ProHandle = "eshop-pro";
    private const string BasicHandle = "basic-plan";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IMeteredComponentValidator _validator = Substitute.For<IMeteredComponentValidator>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _service = new SubscriptionService(_billingClient, _validator, _publisher,
            Substitute.For<IAppLogger<SubscriptionService>>());

        _validator.GetValidatedComponentAsync(Arg.Any<CancellationToken>()).Returns(MeteredComponent());
    }

    private static MeteredComponent MeteredComponent() =>
        new(3057195, "api-call", "API Calls", ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent.MeteredKind,
            "per_unit", 1L, "call");

    private static BillingPlan Plan(string handle, long priceInCents, bool archived = false) =>
        new(1, handle, handle, null, priceInCents, 1, "month", archived);

    private static CustomerSubscription Subscription(SubscriptionStatus status = SubscriptionStatus.Active,
        string planHandle = ProHandle,
        string? customerReference = UserReference,
        int id = 90001) =>
        new(id, status, status.ToString().ToLowerInvariant(), 5001, customerReference, planHandle, planHandle,
            29900L, null, null, null, null, null, false, null);

    // ---------- UC1: subscribe ----------

    [Fact]
    public async Task SubscribingCreatesTheProviderCustomerWhenTheUserHasNone()
    {
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(Plan(ProHandle, 29900L));
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _billingClient.CreateCustomerAsync(UserReference, UserReference, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(new BillingCustomer(5001, UserReference, UserReference, "buyer", "x"));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(5001, ProHandle, Arg.Any<CancellationToken>()).Returns(Subscription());

        var subscription = await _service.SubscribeAsync(UserReference, ProHandle);

        Assert.Equal(90001, subscription.Id);
        await _billingClient.Received(1).CreateCustomerAsync(UserReference, UserReference, Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingReusesAnExistingProviderCustomer()
    {
        ArrangeExistingCustomer();
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(Plan(ProHandle, 29900L));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(5001, ProHandle, Arg.Any<CancellationToken>()).Returns(Subscription());

        await _service.SubscribeAsync(UserReference, ProHandle);

        await _billingClient.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepeatedSubscribeReturnsTheExistingSubscriptionInsteadOfCreatingASecond()
    {
        ArrangeExistingCustomer();
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(Plan(ProHandle, 29900L));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });

        var subscription = await _service.SubscribeAsync(UserReference, ProHandle);

        Assert.Equal(90001, subscription.Id);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnresolvablePlanHandleIsAConfigurationFailureAndNothingIsEnrolled()
    {
        _billingClient.FindPlanByHandleAsync("gone", Arg.Any<CancellationToken>()).Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _service.SubscribeAsync(UserReference, "gone"));

        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnArchivedPlanCannotBeSubscribedTo()
    {
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>())
            .Returns(Plan(ProHandle, 29900L, archived: true));

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _service.SubscribeAsync(UserReference, ProHandle));
    }

    [Fact]
    public async Task ASuccessfulEnrollmentPublishesTheActivationNotification()
    {
        ArrangeExistingCustomer();
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(Plan(ProHandle, 29900L));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(5001, ProHandle, Arg.Any<CancellationToken>()).Returns(Subscription());

        await _service.SubscribeAsync(UserReference, ProHandle);

        await _publisher.Received(1).Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingNotificationHandlerNeverRollsBackTheEnrollment()
    {
        ArrangeExistingCustomer();
        _billingClient.FindPlanByHandleAsync(ProHandle, Arg.Any<CancellationToken>()).Returns(Plan(ProHandle, 29900L));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _billingClient.CreateSubscriptionAsync(5001, ProHandle, Arg.Any<CancellationToken>()).Returns(Subscription());
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("handler blew up"));

        var subscription = await _service.SubscribeAsync(UserReference, ProHandle);

        Assert.Equal(90001, subscription.Id);
    }

    [Fact]
    public async Task AUserWithNoProviderCustomerHasNoSubscriptions()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        Assert.Empty(await _service.ListSubscriptionsAsync(UserReference));
    }

    // ---------- Ownership ----------

    [Fact]
    public async Task AnotherCustomersSubscriptionIsIndistinguishableFromOneThatDoesNotExist()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(customerReference: "someone-else@example.com"));

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _service.RecordUsageAsync(UserReference, 90001, 1m, null));
    }

    [Fact]
    public async Task AnAdministratorMayActOnAnotherCustomersSubscription()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(customerReference: "someone-else@example.com"));
        ArrangeUsageAccepted();

        var report = await _service.RecordUsageForAnyCustomerAsync(90001, 1m, null);

        Assert.Equal(90001, report.SubscriptionId);
    }

    // ---------- UC2: usage ----------

    [Fact]
    public async Task RecordingUsageReadsBackTheRunningTotal()
    {
        ArrangeOwnedActiveSubscription();
        ArrangeUsageAccepted();
        _billingClient.GetComponentUsageAsync(90001, Arg.Any<MeteredComponent>(), Arg.Any<CancellationToken>())
            .Returns(new ComponentUsageSummary(3057195, "api-call", "API Calls", 25, 1L));

        var report = await _service.RecordUsageAsync(UserReference, 90001, 5m, "batch");

        Assert.True(report.IsTotalAvailable);
        Assert.Equal(25, report.Usage!.UnitBalance);
        Assert.Equal(0.25m, report.Usage.EstimatedCharge);
    }

    [Fact]
    public async Task AFailedReadBackLeavesTheUsageRecordedRatherThanFailingTheOperation()
    {
        ArrangeOwnedActiveSubscription();
        ArrangeUsageAccepted();
        _billingClient.GetComponentUsageAsync(90001, Arg.Any<MeteredComponent>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("GetComponentUsage", "timeout"));

        var report = await _service.RecordUsageAsync(UserReference, 90001, 5m, null);

        Assert.False(report.IsTotalAvailable);
        Assert.Null(report.Usage);
        Assert.Equal(5m, report.Recorded.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveQuantityIsRejectedBeforeAnyProviderCall(int quantity)
    {
        ArrangeOwnedActiveSubscription();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(UserReference, 90001, quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsageCannotBeRecordedOnASubscriptionThatIsNotActive()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(UserReference, 90001, 1m, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMisconfiguredComponentStopsUsageBeforeItReachesTheProvider()
    {
        ArrangeOwnedActiveSubscription();
        _validator.GetValidatedComponentAsync(Arg.Any<CancellationToken>())
            .Throws(new BillingConfigurationException("component is not metered"));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.RecordUsageAsync(UserReference, 90001, 1m, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadingUsageForAnOwnedSubscriptionReturnsTheRunningTotal()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.GetComponentUsageAsync(90001, Arg.Any<MeteredComponent>(), Arg.Any<CancellationToken>())
            .Returns(new ComponentUsageSummary(3057195, "api-call", "API Calls", 12, 1L));

        var usage = await _service.GetUsageAsync(UserReference, 90001);

        Assert.NotNull(usage);
        Assert.Equal(12, usage!.UnitBalance);
        Assert.Equal(0.12m, usage.EstimatedCharge);
    }

    [Fact]
    public async Task ReadingUsageForAnotherCustomersSubscriptionIsNotFound()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(customerReference: "someone-else@example.com"));

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => _service.GetUsageAsync(UserReference, 90001));
    }

    [Fact]
    public async Task AnAdministratorMayTransitionAnotherCustomersSubscription()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(customerReference: "someone-else@example.com"));
        _billingClient.PauseSubscriptionAsync(90001, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.OnHold, customerReference: "someone-else@example.com"));

        var subscription = await _service.ApplyLifecycleActionForAnyCustomerAsync(90001,
            SubscriptionLifecycleAction.Pause, CancellationTiming.Immediate, null);

        Assert.Equal(SubscriptionStatus.OnHold, subscription.Status);
        await _publisher.Received(1).Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdministratorStillCannotApplyAnIllegalTransition()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.Canceled, customerReference: "someone-else@example.com"));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionForAnyCustomerAsync(90001, SubscriptionLifecycleAction.Pause,
                CancellationTiming.Immediate, null));
    }

    [Fact]
    public async Task AnAdministratorActingOnAnUnknownSubscriptionIsNotFound()
    {
        _billingClient.GetSubscriptionAsync(404404, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _service.RecordUsageForAnyCustomerAsync(404404, 1m, null));
    }

    [Fact]
    public async Task AnAtRenewalPreviewIsPassedThroughToTheProviderWithItsTiming()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.FindPlanByHandleAsync(BasicHandle, Arg.Any<CancellationToken>()).Returns(Plan(BasicHandle, 2900L));
        _billingClient.PreviewPlanChangeAsync(90001, ProHandle, BasicHandle, PlanChangeTiming.AtNextRenewal,
                Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview(ProHandle, BasicHandle, PlanChangeTiming.AtNextRenewal, 0L, 2900L, 0L, 0L));

        var preview = await _service.PreviewPlanChangeAsync(UserReference, 90001, BasicHandle,
            PlanChangeTiming.AtNextRenewal);

        Assert.Equal(PlanChangeTiming.AtNextRenewal, preview.Timing);
        Assert.Equal(0L, preview.PaymentDueInCents);
    }

    [Fact]
    public async Task AnUnresolvableTargetPlanIsAConfigurationFailureRatherThanAProviderCall()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.FindPlanByHandleAsync(BasicHandle, Arg.Any<CancellationToken>()).Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.PreviewPlanChangeAsync(UserReference, 90001, BasicHandle, PlanChangeTiming.Immediate));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOrderHookRecordsNothingWhenTheShopperHasNoBillingCustomer()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        Assert.Null(await _service.TryRecordUsageForUserAsync(UserReference, 1m, "order 1"));
    }

    [Fact]
    public async Task TheOrderHookRecordsNothingWhenTheShopperHasNoSubscription()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(5001, UserReference, UserReference, "buyer", "x"));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(SubscriptionStatus.Canceled) });

        Assert.Null(await _service.TryRecordUsageForUserAsync(UserReference, 1m, "order 1"));
    }

    [Fact]
    public async Task TheOrderHookRecordsOneUnitAgainstAnActiveSubscription()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(5001, UserReference, UserReference, "buyer", "x"));
        _billingClient.ListSubscriptionsForCustomerAsync(5001, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });
        ArrangeUsageAccepted();

        var report = await _service.TryRecordUsageForUserAsync(UserReference, 1m, "order 1");

        Assert.NotNull(report);
        await _billingClient.Received(1).RecordUsageAsync(90001, "api-call", 1m, "order 1", Arg.Any<CancellationToken>());
    }

    // ---------- UC3: plan change ----------

    [Fact]
    public async Task ChangingToTheSamePlanIsRejectedAsANoOpBeforeAnyProviderCall()
    {
        ArrangeOwnedActiveSubscription();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(UserReference, 90001, ProHandle, PlanChangeTiming.Immediate));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACancelledSubscriptionCannotChangePlan()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(UserReference, 90001, BasicHandle, PlanChangeTiming.Immediate));

        Assert.Contains("Reactivate", exception.Message);
    }

    [Fact]
    public async Task CommittingThePreviewedAmountAppliesTheChangeAndAnnouncesIt()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.FindPlanByHandleAsync(BasicHandle, Arg.Any<CancellationToken>()).Returns(Plan(BasicHandle, 2900L));
        _billingClient.PreviewPlanChangeAsync(90001, ProHandle, BasicHandle, PlanChangeTiming.Immediate,
            Arg.Any<CancellationToken>()).Returns(Preview(28400L));
        _billingClient.ChangePlanAsync(90001, BasicHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: BasicHandle));

        var subscription = await _service.ChangePlanAsync(UserReference, 90001, BasicHandle,
            PlanChangeTiming.Immediate, confirmedPaymentDueInCents: 28400L);

        Assert.Equal(BasicHandle, subscription.PlanHandle);
        await _publisher.Received(1).Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACostThatMovedSincePreviewRefusesTheCommitAndChangesNothing()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.FindPlanByHandleAsync(BasicHandle, Arg.Any<CancellationToken>()).Returns(Plan(BasicHandle, 2900L));
        _billingClient.PreviewPlanChangeAsync(90001, ProHandle, BasicHandle, PlanChangeTiming.Immediate,
            Arg.Any<CancellationToken>()).Returns(Preview(31000L));

        var exception = await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(UserReference, 90001, BasicHandle, PlanChangeTiming.Immediate, 28400L));

        Assert.Equal(28400L, exception.ConfirmedPaymentDueInCents);
        Assert.Equal(31000L, exception.CurrentPaymentDueInCents);

        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    // ---------- UC4: lifecycle ----------

    [Theory]
    [InlineData(SubscriptionStatus.OnHold, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionStatus.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionStatus.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionStatus.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionStatus.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionStatus.Canceled, SubscriptionLifecycleAction.Cancel)]
    public async Task AnIllegalTransitionIsRejectedWithoutCallingTheProvider(SubscriptionStatus from,
        SubscriptionLifecycleAction action)
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>()).Returns(Subscription(from));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(UserReference, 90001, action, CancellationTiming.Immediate, null));

        Assert.Contains("Legal actions", exception.Message);

        await _billingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CancelSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PausingAnActiveSubscriptionAnnouncesTheOldAndNewState()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.PauseSubscriptionAsync(90001, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.OnHold));

        await _service.ApplyLifecycleActionAsync(UserReference, 90001, SubscriptionLifecycleAction.Pause,
            CancellationTiming.Immediate, null);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.PreviousStatus == SubscriptionStatus.Active &&
                                                  n.NewStatus == SubscriptionStatus.OnHold),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingAtTheEndOfThePeriodPassesTheChosenTimingThrough()
    {
        ArrangeOwnedActiveSubscription();
        _billingClient.CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, "switching",
            Arg.Any<CancellationToken>()).Returns(Subscription());

        await _service.ApplyLifecycleActionAsync(UserReference, 90001, SubscriptionLifecycleAction.Cancel,
            CancellationTiming.EndOfPeriod, "switching");

        await _billingClient.Received(1).CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, "switching",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatingACancelledSubscriptionIsLegal()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.Canceled));
        _billingClient.ReactivateSubscriptionAsync(90001, Arg.Any<CancellationToken>()).Returns(Subscription());

        var subscription = await _service.ApplyLifecycleActionAsync(UserReference, 90001,
            SubscriptionLifecycleAction.Reactivate, CancellationTiming.Immediate, null);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task ALifecycleActionOnAnUnknownSubscriptionIsNotFound()
    {
        _billingClient.GetSubscriptionAsync(404404, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _service.ApplyLifecycleActionAsync(UserReference, 404404, SubscriptionLifecycleAction.Cancel,
                CancellationTiming.Immediate, null));
    }

    private static PlanChangePreview Preview(long paymentDueInCents) =>
        new(ProHandle, BasicHandle, PlanChangeTiming.Immediate, -1500L, 2900L, paymentDueInCents, 1500L);

    private void ArrangeExistingCustomer()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(5001, UserReference, UserReference, "buyer", "x"));
    }

    private void ArrangeOwnedActiveSubscription()
    {
        _billingClient.GetSubscriptionAsync(90001, Arg.Any<CancellationToken>()).Returns(Subscription());
    }

    private void ArrangeUsageAccepted()
    {
        _billingClient.RecordUsageAsync(90001, "api-call", Arg.Any<decimal>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new UsageRecord(900123L, callInfo.ArgAt<decimal>(2), callInfo.ArgAt<string?>(3),
                3057195, "api-call", 90001, DateTimeOffset.UtcNow));
    }
}
