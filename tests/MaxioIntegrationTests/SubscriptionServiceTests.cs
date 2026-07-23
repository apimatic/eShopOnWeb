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
/// The orchestration half of the seam: the rules that must hold regardless of which billing
/// provider sits behind <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionServiceTests
{
    private const string Reference = "demouser@microsoft.com";
    private const string ProHandle = "eshop-pro";
    private const string BasicHandle = "basic-plan";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _service = new SubscriptionService(
            _billingClient, _publisher, Substitute.For<IAppLogger<SubscriptionService>>());
    }

    private static BillingPlan Plan(string handle = ProHandle, decimal price = 299m, bool archived = false) =>
        new() { Id = 1, Handle = handle, Name = handle, Price = price, Interval = 1, IntervalUnit = "month", IsArchived = archived };

    private static BillingSubscription Subscription(
        int id = 900001,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = ProHandle) =>
        new()
        {
            Id = id,
            State = state,
            PlanHandle = planHandle,
            CustomerId = 51234,
            CustomerReference = Reference,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20)
        };

    private void PlanExists(string handle, bool archived = false) =>
        _billingClient.FindPlanByHandleAsync(handle, Arg.Any<CancellationToken>())
            .Returns(Plan(handle, archived: archived));

    private void UserHas(params BillingSubscription[] subscriptions) =>
        _billingClient.ListSubscriptionsAsync(Reference, Arg.Any<CancellationToken>())
            .Returns(subscriptions);

    private void SubscriptionExists(BillingSubscription subscription) =>
        _billingClient.GetSubscriptionAsync(subscription.Id, Arg.Any<CancellationToken>())
            .Returns(subscription);

    // -------------------------------------------------------------------------------------------
    // UC1 — Subscribe
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_EnrollsTheCustomerAndPublishesActivation()
    {
        PlanExists(ProHandle);
        UserHas();
        _billingClient.EnsureCustomerAsync(Arg.Any<SubscriberIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 51234, Reference = Reference });
        _billingClient.CreateSubscriptionAsync(51234, ProHandle, Arg.Any<CancellationToken>())
            .Returns(Subscription());

        var result = await _service.SubscribeAsync(new SubscriberIdentity(Reference), ProHandle);

        Assert.Equal(900001, result.Id);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.UserReference == Reference && n.Subscription.Id == 900001),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingActiveSubscription_WithoutEnrollingTwice()
    {
        PlanExists(ProHandle);
        UserHas(Subscription());

        var result = await _service.SubscribeAsync(new SubscriberIdentity(Reference), ProHandle);

        Assert.Equal(900001, result.Id);

        // The heart of the duplicate-subscribe guard: no second enrollment, no customer touched.
        await _billingClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default);
        await _billingClient.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_EnrollsWhenTheUsersOnlySubscriptionIsCancelled()
    {
        PlanExists(ProHandle);
        UserHas(Subscription(state: SubscriptionState.Canceled));
        _billingClient.EnsureCustomerAsync(Arg.Any<SubscriberIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 51234, Reference = Reference });
        _billingClient.CreateSubscriptionAsync(51234, ProHandle, Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 900002));

        var result = await _service.SubscribeAsync(new SubscriberIdentity(Reference), ProHandle);

        Assert.Equal(900002, result.Id);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_AndCreatesNoCustomer_WhenThePlanHandleDoesNotResolve()
    {
        _billingClient.FindPlanByHandleAsync("stale-handle", Arg.Any<CancellationToken>())
            .Returns((BillingPlan?)null);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(new SubscriberIdentity(Reference), "stale-handle"));

        Assert.Contains("stale-handle", ex.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default);
        await _billingClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenThePlanIsArchived()
    {
        PlanExists(ProHandle, archived: true);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(new SubscriberIdentity(Reference), ProHandle));

        await _billingClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_StillSucceeds_WhenTheNotificationHandlerFails()
    {
        PlanExists(ProHandle);
        UserHas();
        _billingClient.EnsureCustomerAsync(Arg.Any<SubscriberIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 51234, Reference = Reference });
        _billingClient.CreateSubscriptionAsync(51234, ProHandle, Arg.Any<CancellationToken>())
            .Returns(Subscription());

        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        // Eventing is best-effort: a handler failure must never undo a committed enrollment.
        var result = await _service.SubscribeAsync(new SubscriberIdentity(Reference), ProHandle);

        Assert.Equal(900001, result.Id);
    }

    [Fact]
    public async Task GetActiveSubscriptionAsync_ReturnsNull_WhenNoneAreActive()
    {
        UserHas(Subscription(state: SubscriptionState.Canceled), Subscription(id: 2, state: SubscriptionState.Expired));

        Assert.Null(await _service.GetActiveSubscriptionAsync(Reference));
    }

    // -------------------------------------------------------------------------------------------
    // UC2 — Usage
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public async Task RecordUsageAsync_RejectsNonPositiveQuantities_BeforeAnyProviderCall(decimal quantity)
    {
        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(900001, quantity, "memo"));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().GetSubscriptionAsync(default, default);
    }

    [Fact]
    public async Task RecordUsageAsync_Rejects_WhenTheSubscriptionDoesNotExist()
    {
        _billingClient.GetSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(900001, 1, null));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Expired)]
    [InlineData(SubscriptionState.PastDue)]
    public async Task RecordUsageAsync_Rejects_WhenTheSubscriptionIsNotActive(SubscriptionState state)
    {
        SubscriptionExists(Subscription(state: state));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(900001, 1, null));

        Assert.Contains(state.ToString(), ex.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default, default);
    }

    [Fact]
    public async Task RecordUsageAsync_MetersAnActiveSubscription()
    {
        SubscriptionExists(Subscription());
        _billingClient.RecordUsageAsync(900001, 3, "order 42", Arg.Any<CancellationToken>())
            .Returns(new UsageRecordResult
            {
                UsageId = 1,
                SubscriptionId = 900001,
                ComponentHandle = "api-call",
                Quantity = 3,
                PeriodToDateUnits = 9
            });

        var result = await _service.RecordUsageAsync(900001, 3, "order 42");

        Assert.Equal(3m, result.Quantity);
        Assert.Equal(9, result.PeriodToDateUnits);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_Rejects_WhenTheUserHasNoActiveSubscription()
    {
        UserHas();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageForUserAsync(Reference, 1, null));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default, default);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_MetersTheUsersActiveSubscription()
    {
        UserHas(Subscription());
        SubscriptionExists(Subscription());
        _billingClient.RecordUsageAsync(900001, 1, null, Arg.Any<CancellationToken>())
            .Returns(new UsageRecordResult { SubscriptionId = 900001, ComponentHandle = "api-call", Quantity = 1 });

        var result = await _service.RecordUsageForUserAsync(Reference, 1, null);

        Assert.Equal(900001, result.SubscriptionId);
    }

    // -------------------------------------------------------------------------------------------
    // UC3 — Plan change
    // -------------------------------------------------------------------------------------------

    private PlanChangePreview PreviewFor(decimal paymentDue) => new()
    {
        SubscriptionId = 900001,
        CurrentPlanHandle = ProHandle,
        TargetPlanHandle = BasicHandle,
        Timing = PlanChangeTiming.Immediate,
        PaymentDue = paymentDue,
        TargetPlanPrice = 29m
    };

    [Fact]
    public async Task ChangePlanAsync_CommitsWhenTheQuoteStillMatches_AndPublishes()
    {
        SubscriptionExists(Subscription());
        PlanExists(BasicHandle);

        var quote = PreviewFor(240m);
        _billingClient.PreviewPlanChangeAsync(900001, BasicHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(quote);
        _billingClient.ChangePlanAsync(900001, BasicHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: BasicHandle));

        var result = await _service.ChangePlanAsync(900001, BasicHandle, PlanChangeTiming.Immediate, quote.Fingerprint);

        Assert.Equal(BasicHandle, result.PlanHandle);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n =>
                n.PreviousPlanHandle == ProHandle && n.NewPlanHandle == BasicHandle && n.ProrationAmount == 240m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePlanAsync_RefusesAStaleQuote_AndCommitsNothing()
    {
        SubscriptionExists(Subscription());
        PlanExists(BasicHandle);

        var quotedToCustomer = PreviewFor(240m);

        // The basis moved between preview and confirm.
        _billingClient.PreviewPlanChangeAsync(900001, BasicHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(PreviewFor(310m));

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _service.ChangePlanAsync(900001, BasicHandle, PlanChangeTiming.Immediate, quotedToCustomer.Fingerprint));

        // The customer is never charged an amount other than the one they confirmed.
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
        await _publisher.DidNotReceiveWithAnyArgs().Publish(Arg.Any<SubscriptionPlanChanged>(), default);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsANoOpChange_BeforeAnyProviderCall()
    {
        SubscriptionExists(Subscription(planHandle: ProHandle));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(900001, ProHandle, PlanChangeTiming.Immediate, "any"));

        await _billingClient.DidNotReceiveWithAnyArgs().PreviewPlanChangeAsync(default, default!, default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAChangeOnACancelledSubscription()
    {
        SubscriptionExists(Subscription(state: SubscriptionState.Canceled));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(900001, BasicHandle, PlanChangeTiming.Immediate, "any"));

        Assert.Contains("Reactivate", ex.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Throws_WhenTheTargetPlanDoesNotResolve()
    {
        SubscriptionExists(Subscription());
        _billingClient.FindPlanByHandleAsync("gone", Arg.Any<CancellationToken>()).Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.PreviewPlanChangeAsync(900001, "gone", PlanChangeTiming.Immediate));

        await _billingClient.DidNotReceiveWithAnyArgs().PreviewPlanChangeAsync(default, default!, default, default);
    }

    // -------------------------------------------------------------------------------------------
    // UC4 — Lifecycle
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Reactivate)]
    public async Task ApplyLifecycleActionAsync_RejectsIllegalTransitions_WithoutCallingTheProvider(
        SubscriptionState currentState,
        SubscriptionLifecycleAction action)
    {
        SubscriptionExists(Subscription(state: currentState));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(900001, action));

        // The message tells the caller the current state, as the use case requires.
        Assert.Contains(currentState.ToString(), ex.Message);

        await _billingClient.DidNotReceiveWithAnyArgs().PauseSubscriptionAsync(default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().ResumeSubscriptionAsync(default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().CancelSubscriptionAsync(default, default, default, default);
        await _billingClient.DidNotReceiveWithAnyArgs().ReactivateSubscriptionAsync(default, default);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_PausesAnActiveSubscription_AndPublishes()
    {
        SubscriptionExists(Subscription(state: SubscriptionState.Active));
        _billingClient.PauseSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Paused));

        var result = await _service.ApplyLifecycleActionAsync(900001, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.Paused, result.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n =>
                n.PreviousState == SubscriptionState.Active &&
                n.NewState == SubscriptionState.Paused &&
                n.Action == SubscriptionLifecycleAction.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_ResumesAPausedSubscription()
    {
        SubscriptionExists(Subscription(state: SubscriptionState.Paused));
        _billingClient.ResumeSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Active));

        var result = await _service.ApplyLifecycleActionAsync(900001, SubscriptionLifecycleAction.Resume);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_ReactivatesACancelledSubscription()
    {
        SubscriptionExists(Subscription(state: SubscriptionState.Canceled));
        _billingClient.ReactivateSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Active));

        var result = await _service.ApplyLifecycleActionAsync(900001, SubscriptionLifecycleAction.Reactivate);

        Assert.Equal(SubscriptionState.Active, result.State);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_PassesTheCancellationTimingThrough()
    {
        SubscriptionExists(Subscription());
        _billingClient.CancelSubscriptionAsync(900001, CancellationTiming.EndOfPeriod, "leaving", Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Active));

        await _service.ApplyLifecycleActionAsync(
            900001, SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod, "leaving");

        await _billingClient.Received(1).CancelSubscriptionAsync(
            900001, CancellationTiming.EndOfPeriod, "leaving", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_SurfacesADriftedState_WhenTheProviderRejectsAnAllowedTransition()
    {
        // Local view says active, so pausing looks legal...
        _billingClient.GetSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns(
                Subscription(state: SubscriptionState.Active),
                // ...but the re-read after the failure reveals it was cancelled out of band.
                Subscription(state: SubscriptionState.Canceled));

        _billingClient.PauseSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("Subscription is canceled.", 422));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => _service.ApplyLifecycleActionAsync(900001, SubscriptionLifecycleAction.Pause));

        // The provider's state is treated as the truth and reported back.
        Assert.Contains("Canceled", ex.Message);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_Throws_WhenTheSubscriptionDoesNotExist()
    {
        _billingClient.GetSubscriptionAsync(999999, Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(999999, SubscriptionLifecycleAction.Pause));
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_StillSucceeds_WhenTheNotificationHandlerFails()
    {
        SubscriptionExists(Subscription(state: SubscriptionState.Active));
        _billingClient.PauseSubscriptionAsync(900001, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Paused));
        _publisher.Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        var result = await _service.ApplyLifecycleActionAsync(900001, SubscriptionLifecycleAction.Pause);

        Assert.Equal(SubscriptionState.Paused, result.State);
    }
}
