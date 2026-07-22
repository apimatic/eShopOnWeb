using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The use-case layer sitting on the provider-agnostic seam: what it validates locally, what it
/// forwards, and what it announces in-process.
/// </summary>
public class SubscriptionServiceTests
{
    private static readonly SubscriptionActor Customer = new(BillingClientFixture.UserReference, false);
    private static readonly SubscriptionActor Administrator = new("admin@microsoft.com", true);
    private static readonly SubscriptionActor Stranger = new("someone.else@microsoft.com", false);

    private static (SubscriptionService Service, FakeBillingClient Billing, RecordingPublisher Publisher)
        Create(Action<FakeBillingClient>? arrange = null)
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(FakeBillingClient.Plan("eshop-pro", 299.00m));
        billing.Plans.Add(FakeBillingClient.Plan("basic-plan", 29.00m));
        arrange?.Invoke(billing);

        var publisher = new RecordingPublisher();
        var service = new SubscriptionService(billing, publisher, new TestLogger<SubscriptionService>());

        return (service, billing, publisher);
    }

    [Fact]
    public async Task SubscribingCreatesTheCustomerRecordOnFirstUseAndAnnouncesTheActivation()
    {
        var (service, billing, publisher) = Create(fake =>
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active));

        var subscription = await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.True(billing.Called("CreateCustomerAsync"));
        Assert.True(billing.Called("CreateSubscriptionAsync"));

        var activated = publisher.Single<SubscriptionActivated>();
        Assert.Equal(BillingClientFixture.UserReference, activated.UserReference);
        Assert.Equal(15236915, activated.Subscription.Id);
    }

    [Fact]
    public async Task ARepeatedSubscribeReturnsTheExistingEnrolmentInsteadOfCreatingASecond()
    {
        var existing = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
        var (service, billing, publisher) = Create(fake =>
        {
            fake.Customer = new BillingCustomer(88001, BillingClientFixture.UserReference,
                BillingClientFixture.UserReference, "demouser", "eShopOnWeb");
            fake.CustomerSubscriptions.Add(existing);
        });

        var subscription = await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.Equal(existing.Id, subscription.Id);
        Assert.False(billing.Called("CreateSubscriptionAsync"));
        Assert.False(billing.Called("CreateCustomerAsync"));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task ACancelledSubscriptionDoesNotBlockANewEnrolment()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Customer = new BillingCustomer(88001, BillingClientFixture.UserReference,
                BillingClientFixture.UserReference, "demouser", "eShopOnWeb");
            fake.CustomerSubscriptions.Add(
                FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Canceled));
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
        });

        await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.True(billing.Called("CreateSubscriptionAsync"));
    }

    [Fact]
    public async Task AnUnknownPlanIsRejectedWithoutEnrollingAnybody()
    {
        var (service, billing, _) = Create();

        await Assert.ThrowsAsync<InvalidBillingRequestException>(
            () => service.SubscribeAsync(Customer, "no-such-plan"));

        Assert.False(billing.Called("CreateSubscriptionAsync"));
    }

    [Fact]
    public async Task AUserWithNoBillingRecordSeesAnEmptyListRatherThanAnError()
    {
        var (service, _, _) = Create();

        Assert.Empty(await service.ListMySubscriptionsAsync(Customer));
    }

    [Fact]
    public async Task ACustomerCannotActOnSomebodyElsesSubscription()
    {
        var (service, billing, _) = Create(fake =>
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active));

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(
            () => service.ApplyLifecycleActionAsync(Stranger, 15236915, SubscriptionLifecycleAction.Cancel,
                SubscriptionCancellationTiming.Immediate, null));

        Assert.False(billing.Called("CancelSubscriptionAsync"));
    }

    [Fact]
    public async Task AnAdministratorMayActOnAnySubscription()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Canceled);
        });

        var result = await service.ApplyLifecycleActionAsync(Administrator, 15236915,
            SubscriptionLifecycleAction.Cancel, SubscriptionCancellationTiming.Immediate, "fraud");

        Assert.Equal(BillingSubscriptionState.Canceled, result.NewState);
        Assert.True(billing.Called("CancelSubscriptionAsync"));
    }

    [Fact]
    public async Task ReadingASubscriptionTheProviderDoesNotHaveIsNullNotAnError()
    {
        var (service, _, _) = Create();

        Assert.Null(await service.GetSubscriptionAsync(Customer, 404404));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task UsageIsRejectedBeforeAnyProviderCallWhenTheQuantityIsNotPositive(int quantity)
    {
        var (service, billing, _) = Create();

        await Assert.ThrowsAsync<InvalidBillingRequestException>(
            () => service.RecordUsageAsync(Customer, 15236915, quantity, null));

        Assert.True(billing.WasNeverCalled);
    }

    [Fact]
    public async Task UsageOnASubscriptionThatIsNotActiveIsRefused()
    {
        var (service, billing, _) = Create(fake =>
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.OnHold));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.RecordUsageAsync(Customer, 15236915, 5m, null));

        Assert.Equal(BillingSubscriptionState.OnHold, exception.CurrentState);
        Assert.False(billing.Called("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordedUsageIsPricedAtTheComponentsUnitPrice()
    {
        var (service, _, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.PeriodToDateUsage = 250m;
        });

        var report = await service.RecordUsageAsync(Customer, 15236915, 7m, "order 42");

        Assert.Equal(7m, report.Record.Quantity);
        Assert.Equal(250m, report.PeriodToDateQuantity);
        Assert.Equal(0.01m, report.UnitPrice);
        Assert.Equal(2.50m, report.EstimatedPeriodToDateAmount);
        Assert.True(report.PeriodToDateAvailable);
    }

    [Fact]
    public async Task WhenTheRunningTotalCannotBeReadTheUsageStillStands()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.PeriodToDateFailure = FakeBillingClient.ProviderDown;
        });

        var report = await service.RecordUsageAsync(Customer, 15236915, 3m, null);

        Assert.Equal(3m, report.Record.Quantity);
        Assert.False(report.PeriodToDateAvailable);
        Assert.Null(report.PeriodToDateQuantity);
        Assert.Null(report.EstimatedPeriodToDateAmount);
        Assert.True(billing.Called("RecordUsageAsync"));
    }

    [Fact]
    public async Task MovingToThePlanTheSubscriptionIsAlreadyOnIsRejectedAsANoOp()
    {
        var (service, billing, _) = Create(fake =>
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active));

        await Assert.ThrowsAsync<InvalidBillingRequestException>(
            () => service.PreviewPlanChangeAsync(Customer, 15236915, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.False(billing.Called("PreviewPlanChangeAsync"));
    }

    [Fact]
    public async Task APlanChangeIsRefusedWhileTheSubscriptionIsCancelled()
    {
        var (service, billing, _) = Create(fake =>
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ChangePlanAsync(Customer, 15236915, "basic-plan", PlanChangeTiming.Immediate, null));

        Assert.Equal(BillingSubscriptionState.Canceled, exception.CurrentState);
        Assert.False(billing.Called("MigratePlanAsync"));
    }

    [Fact]
    public async Task ADeferredPlanChangeIsQuotedAtZeroAndTakesEffectAtThePeriodEnd()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.Quote = new PlanMigrationQuote(-40m, 100m, 60m, 40m);
        });

        var preview = await service.PreviewPlanChangeAsync(Customer, 15236915, "basic-plan",
            PlanChangeTiming.NextRenewal);

        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.False(billing.Called("PreviewPlanChangeAsync"));
    }

    [Fact]
    public async Task AStalePreviewIsRefusedRatherThanChargingADifferentAmount()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.Quote = new PlanMigrationQuote(0m, 100m, 60m, 40m);
        });

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ChangePlanAsync(Customer, 15236915, "basic-plan", PlanChangeTiming.Immediate,
                previewedPaymentDue: 12.00m));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(billing.Called("MigratePlanAsync"));
    }

    [Fact]
    public async Task AConfirmedPreviewCommitsAtTheQuotedAmountAndAnnouncesTheChange()
    {
        var (service, _, publisher) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active,
                "basic-plan");
            fake.Quote = new PlanMigrationQuote(0m, 100m, 60m, 40m);
        });

        var result = await service.ChangePlanAsync(Customer, 15236915, "basic-plan", PlanChangeTiming.Immediate,
            previewedPaymentDue: 60.00m);

        Assert.Equal("eshop-pro", result.PreviousPlanHandle);
        Assert.Equal("basic-plan", result.NewPlanHandle);
        Assert.Equal(60.00m, result.AppliedPaymentDue);

        var changed = publisher.Single<SubscriptionPlanChanged>();
        Assert.Equal("eshop-pro", changed.PreviousPlanHandle);
        Assert.Equal("basic-plan", changed.NewPlanHandle);
        Assert.Equal(60.00m, changed.AppliedPaymentDue);
    }

    [Fact]
    public async Task TheCallersOwnPlanIdentifierIsWhatReachesTheProvider()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Plans.Clear();
            // The provider answers a handle lookup with a differently-named record; the caller's
            // choice must survive that, not be overwritten by it.
            fake.Plans.Add(new BillingPlan(7126958, "renamed-by-provider", "Basic", 29.00m, 1, "month", false, false));
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = fake.Subscription;
        });

        billing.Plans.Add(FakeBillingClient.Plan("basic-plan", 29.00m));

        await service.ChangePlanAsync(Customer, 15236915, "basic-plan", PlanChangeTiming.Immediate, null);

        Assert.Equal("basic-plan", billing.LastPlanIdentifierSentToProvider);
    }

    [Theory]
    [InlineData(BillingSubscriptionState.OnHold, SubscriptionLifecycleAction.Pause)]
    [InlineData(BillingSubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(BillingSubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(BillingSubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(BillingSubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel)]
    public async Task AnIllegalTransitionIsRefusedWithoutTouchingTheProvider(BillingSubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        var (service, billing, _) = Create(fake => fake.Subscription = FakeBillingClient.SubscriptionInState(state));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ApplyLifecycleActionAsync(Customer, 15236915, action,
                SubscriptionCancellationTiming.Immediate, null));

        Assert.Equal(state, exception.CurrentState);
        Assert.Contains(action.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "GetSubscriptionAsync:15236915" }, billing.Calls);
    }

    [Fact]
    public async Task PausingAnActiveSubscriptionAnnouncesTheOldAndNewState()
    {
        var (service, _, publisher) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.OnHold);
        });

        var result = await service.ApplyLifecycleActionAsync(Customer, 15236915,
            SubscriptionLifecycleAction.Pause, SubscriptionCancellationTiming.Immediate, null);

        Assert.Equal(BillingSubscriptionState.Active, result.PreviousState);
        Assert.Equal(BillingSubscriptionState.OnHold, result.NewState);

        var announced = publisher.Single<SubscriptionStateChanged>();
        Assert.Equal(BillingSubscriptionState.Active, announced.PreviousState);
        Assert.Equal(BillingSubscriptionState.OnHold, announced.NewState);
        Assert.Equal(SubscriptionLifecycleAction.Pause, announced.Action);
    }

    [Fact]
    public async Task AnEndOfPeriodCancellationDefersRatherThanCancellingNow()
    {
        var (service, billing, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = fake.Subscription with
            {
                CancelAtEndOfPeriod = true,
                ScheduledCancellationAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
            };
        });

        var result = await service.ApplyLifecycleActionAsync(Customer, 15236915,
            SubscriptionLifecycleAction.Cancel, SubscriptionCancellationTiming.EndOfPeriod, "too pricey");

        Assert.True(billing.Called("ScheduleCancellationAsync"));
        Assert.False(billing.Called("CancelSubscriptionAsync"));
        Assert.Equal(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), result.EffectiveAt);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task AProviderThatDidNotActuallyDeferTheCancellationIsSurfacedNotGlossedOver()
    {
        var (service, _, _) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = fake.Subscription;
        });

        var result = await service.ApplyLifecycleActionAsync(Customer, 15236915,
            SubscriptionLifecycleAction.Cancel, SubscriptionCancellationTiming.EndOfPeriod, null);

        Assert.False(result.Subscription.CancelAtEndOfPeriod);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task AFailingNotificationHandlerNeverUndoesAnAppliedBillingChange()
    {
        var (service, billing, publisher) = Create(fake =>
        {
            fake.Subscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.Active);
            fake.UpdatedSubscription = FakeBillingClient.SubscriptionInState(BillingSubscriptionState.OnHold);
        });

        publisher.ThrowOnPublish = true;

        var result = await service.ApplyLifecycleActionAsync(Customer, 15236915,
            SubscriptionLifecycleAction.Pause, SubscriptionCancellationTiming.Immediate, null);

        Assert.Equal(BillingSubscriptionState.OnHold, result.NewState);
        Assert.True(billing.Called("PauseSubscriptionAsync"));
    }
}
