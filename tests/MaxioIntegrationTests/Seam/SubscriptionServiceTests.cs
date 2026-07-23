using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Seam;

/// <summary>
/// The domain orchestration over the provider-agnostic seam: idempotency, transition guards, and
/// the guarantee that eventing never undoes a billing change.
/// </summary>
public class SubscriptionServiceTests
{
    private const string UserReference = "shopper@example.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IMeteredComponentValidator _validator = Substitute.For<IMeteredComponentValidator>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _billingClient.MeteredComponentHandle.Returns("api-call");
        _service = new SubscriptionService(_billingClient, _validator, _publisher, _logger);
    }

    private static BillingSubscription Subscription(SubscriptionStatus status = SubscriptionStatus.Active,
        int id = 93491347,
        string planHandle = "eshop-pro") =>
        new BillingSubscription(id, status, status.ToString().ToLowerInvariant())
        {
            PlanHandle = planHandle,
            CustomerReference = UserReference
        };

    private void GivenCustomerExists(params BillingSubscription[] subscriptions)
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, UserReference, UserReference));

        _billingClient.ListSubscriptionsForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(subscriptions);
    }

    private void GivenPlanExists(string handle) =>
        _billingClient.FindPlanByHandleAsync(handle, Arg.Any<CancellationToken>())
            .Returns(new BillingPlan(1, handle, "Plan", 299m, 1, "month"));

    // --- UC1 ---

    [Fact]
    public async Task SubscribeRefusesAPlanHandleThatDoesNotResolve()
    {
        _billingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>())
            .Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(UserReference, "ghost-plan"));

        // Nothing is enrolled against a guessed plan.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeEnrollsTheCustomerAndAnnouncesTheActivation()
    {
        GivenPlanExists("eshop-pro");
        _billingClient.EnsureCustomerAsync(UserReference, UserReference, null, null, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, UserReference, UserReference));
        _billingClient.ListSubscriptionsForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(UserReference, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription());

        var subscription = await _service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(93491347, subscription.Id);
        await _publisher.Received(1).Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeTwiceReturnsTheExistingSubscriptionRatherThanEnrollingAgain()
    {
        GivenPlanExists("eshop-pro");
        _billingClient.EnsureCustomerAsync(UserReference, UserReference, null, null, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, UserReference, UserReference));
        _billingClient.ListSubscriptionsForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });

        var subscription = await _service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(93491347, subscription.Id);

        // A double-click must never produce a second enrollment.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingNotificationHandlerDoesNotUndoTheSubscription()
    {
        GivenPlanExists("eshop-pro");
        _billingClient.EnsureCustomerAsync(UserReference, UserReference, null, null, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, UserReference, UserReference));
        _billingClient.ListSubscriptionsForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(UserReference, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription());

        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("a handler blew up"));

        // Eventing is best-effort: the subscription stands and the caller still gets it.
        var subscription = await _service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(93491347, subscription.Id);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyForAUserWhoNeverSubscribed()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        Assert.Empty(await _service.GetSubscriptionsForUserAsync(UserReference));

        await _billingClient.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // --- UC2 ---

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task UsageIsRejectedForANonPositiveQuantityBeforeReachingTheProvider(decimal quantity)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _service.RecordUsageAsync(UserReference, quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsageIsRejectedWhenTheUserHasNoSubscription()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => _service.RecordUsageAsync(UserReference, 1, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsageIsRejectedWhenTheSubscriptionIsNotLive()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.OnHold));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.RecordUsageAsync(UserReference, 1, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsageIsValidatedAgainstTheComponentKindThenRecordedWithItsRunningTotal()
    {
        GivenCustomerExists(Subscription());
        _billingClient.RecordUsageAsync(93491347, "api-call", 5m, "batch", Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, 93491347, "api-call", 5m));
        _billingClient.GetPeriodToDateUsageAsync(93491347, "api-call", Arg.Any<CancellationToken>())
            .Returns(205m);

        var usage = await _service.RecordUsageAsync(UserReference, 5m, "batch");

        await _validator.Received(1).EnsureComponentIsMeteredAsync(_billingClient, "api-call",
            Arg.Any<CancellationToken>());

        Assert.Equal(5m, usage.Quantity);
        Assert.Equal(205m, usage.PeriodToDateTotal);
    }

    [Fact]
    public async Task AFailedReadBackStillReportsTheUsageAsRecorded()
    {
        GivenCustomerExists(Subscription());
        _billingClient.RecordUsageAsync(93491347, "api-call", 5m, null, Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, 93491347, "api-call", 5m));
        _billingClient.GetPeriodToDateUsageAsync(93491347, "api-call", Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("read-back failed"));

        var usage = await _service.RecordUsageAsync(UserReference, 5m, null);

        // The units are already billed; the total is simply unavailable.
        Assert.Equal(5m, usage.Quantity);
        Assert.Null(usage.PeriodToDateTotal);
    }

    [Fact]
    public async Task ThePeriodToDateTotalIsReadForTheUsersSubscription()
    {
        GivenCustomerExists(Subscription());
        _billingClient.GetPeriodToDateUsageAsync(93491347, "api-call", Arg.Any<CancellationToken>())
            .Returns(40m);

        Assert.Equal(40m, await _service.GetPeriodToDateUsageAsync(UserReference));
    }

    [Fact]
    public async Task ReadingTheTotalForAUserWithNoSubscriptionIsRejected()
    {
        _billingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => _service.GetPeriodToDateUsageAsync(UserReference));
    }

    // --- UC3 ---

    [Fact]
    public async Task APreviewIsQuotedForTheUsersLiveSubscription()
    {
        GivenCustomerExists(Subscription());
        _billingClient.PreviewPlanChangeAsync(93491347, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview("basic-plan", -299m, 31m, 0m, -268m));

        var preview = await _service.PreviewPlanChangeAsync(UserReference, "basic-plan");

        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(-268m, preview.CreditApplied);
    }

    [Fact]
    public async Task APreviewForTheCurrentPlanIsRejectedBeforeReachingTheProvider()
    {
        GivenCustomerExists(Subscription(planHandle: "eshop-pro"));

        await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => _service.PreviewPlanChangeAsync(UserReference, "eshop-pro"));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlanChangeToAHandleThatDoesNotResolveIsAConfigurationError()
    {
        GivenCustomerExists(Subscription());
        _billingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>())
            .Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.ChangePlanAsync(UserReference, "ghost-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task APlanChangeToTheCurrentPlanIsRejectedBeforeReachingTheProvider()
    {
        GivenCustomerExists(Subscription(planHandle: "eshop-pro"));

        await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => _service.ChangePlanAsync(UserReference, "eshop-pro", PlanChangeTiming.Immediate));

        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlanChangeIsRejectedWhenTheSubscriptionIsNotLive()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.ChangePlanAsync(UserReference, "basic-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task APlanChangeIsRefusedWhenTheCostMovedSinceThePreview()
    {
        GivenCustomerExists(Subscription());
        GivenPlanExists("basic-plan");
        _billingClient.PreviewPlanChangeAsync(93491347, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview("basic-plan", -299m, 31m, 42m, -268m));

        var exception = await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(UserReference, "basic-plan", PlanChangeTiming.Immediate,
                expectedPaymentDue: 0m));

        Assert.Equal(0m, exception.ExpectedPaymentDue);
        Assert.Equal(42m, exception.ActualPaymentDue);

        // The customer is never charged an amount they did not confirm.
        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlanChangeProceedsWhenThePreviewedCostStillHolds()
    {
        GivenCustomerExists(Subscription());
        GivenPlanExists("basic-plan");
        _billingClient.PreviewPlanChangeAsync(93491347, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview("basic-plan", -299m, 31m, 0m, -268m));
        _billingClient.ChangePlanAsync(93491347, "basic-plan", PlanChangeTiming.Immediate,
                Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: "basic-plan"));

        var subscription = await _service.ChangePlanAsync(UserReference, "basic-plan",
            PlanChangeTiming.Immediate, expectedPaymentDue: 0m);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        await _publisher.Received(1).Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARenewalTimedChangeSkipsTheStalenessCheckBecauseItIsNotProrated()
    {
        GivenCustomerExists(Subscription());
        GivenPlanExists("basic-plan");
        _billingClient.ChangePlanAsync(93491347, "basic-plan", PlanChangeTiming.AtNextRenewal,
                Arg.Any<CancellationToken>())
            .Returns(Subscription());

        await _service.ChangePlanAsync(UserReference, "basic-plan", PlanChangeTiming.AtNextRenewal,
            expectedPaymentDue: 999m);

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // --- UC4 ---

    [Fact]
    public async Task ResumingASubscriptionThatIsNotOnHoldIsRejectedWithoutAProviderCall()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Active));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.ResumeAsync(UserReference));

        Assert.Equal(SubscriptionStatus.Active, exception.CurrentStatus);
        await _billingClient.DidNotReceive().ResumeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PausingASubscriptionThatIsNotLiveIsRejectedWithoutAProviderCall()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.PauseAsync(UserReference));

        await _billingClient.DidNotReceive().PauseAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatingAnActiveSubscriptionIsRejectedWithoutAProviderCall()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Active));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.ReactivateAsync(UserReference));

        await _billingClient.DidNotReceive().ReactivateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingAnAlreadyCancelledSubscriptionIsRejectedWithoutAProviderCall()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.CancelAsync(UserReference, CancellationTiming.Immediate));

        await _billingClient.DidNotReceive().CancelAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAnnouncesTheTransitionWithItsPreviousState()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.Active));
        _billingClient.PauseAsync(93491347, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.OnHold));

        await _service.PauseAsync(UserReference);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(notification =>
                notification.PreviousStatus == SubscriptionStatus.Active
                && notification.NewStatus == SubscriptionStatus.OnHold
                && notification.Action == "pause"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeIsAllowedFromOnHold()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.OnHold));
        _billingClient.ResumeAsync(93491347, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.Active));

        var subscription = await _service.ResumeAsync(UserReference);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task AnEndOfPeriodCancelIsRejectedWhenTheSubscriptionIsNotLive()
    {
        GivenCustomerExists(Subscription(SubscriptionStatus.OnHold));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _service.CancelAsync(UserReference, CancellationTiming.EndOfBillingPeriod));
    }

    [Fact]
    public async Task ALiveSubscriptionIsPreferredOverAnOlderCancelledOne()
    {
        GivenCustomerExists(
            Subscription(SubscriptionStatus.Canceled, id: 999),
            Subscription(SubscriptionStatus.Active, id: 100));

        _billingClient.PauseAsync(100, null, Arg.Any<CancellationToken>())
            .Returns(Subscription(SubscriptionStatus.OnHold, id: 100));

        await _service.PauseAsync(UserReference);

        // The live subscription wins even though the cancelled one has a higher id.
        await _billingClient.Received(1).PauseAsync(100, null, Arg.Any<CancellationToken>());
    }
}
