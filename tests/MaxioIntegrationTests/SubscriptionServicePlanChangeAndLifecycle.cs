using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC3 (plan change with a binding preview) and UC4 (lifecycle transitions) rules.
/// </summary>
public class SubscriptionServicePlanChangeAndLifecycle
{
    private readonly SubscriptionServiceHarness _harness = new();

    [Fact]
    public async Task PreviewPlanChangeRejectsAChangeToThePlanAlreadyInUse()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(planHandle: "eshop-pro"));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.PreviewPlanChangeAsync(88001, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", ex.Message, StringComparison.Ordinal);
        await _harness.BillingClient.DidNotReceive().PreviewPlanChangeAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeRejectsACancelledSubscriptionAndSaysWhy()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Canceled));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.PreviewPlanChangeAsync(88001, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Canceled", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Reactivate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPlanChangeRejectsAnUnresolvableTargetPlan()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());
        _harness.BillingClient.FindPlanByHandleAsync("gone-away", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.PreviewPlanChangeAsync(88001, "gone-away", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task PreviewPlanChangeReturnsTheProvidersQuote()
    {
        StubPreviewablePlanChange();

        var preview = await _harness.Service.PreviewPlanChangeAsync(88001, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(50.00m, preview.PaymentDue);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.NotEmpty(preview.Token);
    }

    [Fact]
    public async Task ChangePlanCommitsWhenTheConfirmedQuoteStillStands()
    {
        StubPreviewablePlanChange();
        _harness.BillingClient.ChangePlanAsync(88001, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(planHandle: "basic-plan"));

        var token = SubscriptionServiceHarness.Preview().Token;

        var subscription = await _harness.Service.ChangePlanAsync(
            88001, "basic-plan", PlanChangeTiming.Immediate, token);

        Assert.Equal("basic-plan", subscription.PlanHandle);

        var changed = Assert.IsType<SubscriptionPlanChanged>(Assert.Single(_harness.PublishedNotifications));
        Assert.Equal("eshop-pro", changed.PreviousPlanHandle);
        Assert.Equal("basic-plan", changed.NewPlanHandle);
        Assert.Equal(50.00m, changed.ProrationAmount);
    }

    [Fact]
    public async Task ChangePlanRefusesToCommitAQuoteThatHasMovedSinceItWasShown()
    {
        StubPreviewablePlanChange();

        // The customer confirms a quote taken when the amount due was different.
        var staleToken = SubscriptionServiceHarness.Preview(paymentDue: 12.34m).Token;

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.ChangePlanAsync(88001, "basic-plan", PlanChangeTiming.Immediate, staleToken));

        Assert.Contains("changed since it was previewed", ex.Message, StringComparison.Ordinal);
        // Nothing may be charged when the amount no longer matches what was shown.
        await _harness.BillingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
        Assert.Empty(_harness.PublishedNotifications);
    }

    [Fact]
    public async Task ChangePlanRequiresAPreviewTokenAtAll()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _harness.Service.ChangePlanAsync(88001, "basic-plan", PlanChangeTiming.Immediate, "  "));
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.CancelAtEndOfPeriod)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Resume)]
    public async Task IllegalLifecycleTransitionsAreRejectedWithoutCallingTheProvider(
        SubscriptionState state,
        SubscriptionLifecycleAction action)
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: state));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.ApplyLifecycleActionAsync(88001, action, null));

        Assert.Contains(state.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains("Legal transitions", ex.Message, StringComparison.Ordinal);

        await _harness.BillingClient.DidNotReceive().PauseSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _harness.BillingClient.DidNotReceive().ResumeSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _harness.BillingClient.DidNotReceive().ReactivateSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _harness.BillingClient.DidNotReceive().CancelSubscriptionAtEndOfPeriodAsync(
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PausingAnActiveSubscriptionAnnouncesTheOldAndNewState()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Active));
        _harness.BillingClient.PauseSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Paused));

        var subscription = await _harness.Service.ApplyLifecycleActionAsync(
            88001, SubscriptionLifecycleAction.Pause, "Going on holiday");

        Assert.Equal(SubscriptionState.Paused, subscription.State);

        var changed = Assert.IsType<SubscriptionStateChanged>(Assert.Single(_harness.PublishedNotifications));
        Assert.Equal(SubscriptionState.Active, changed.PreviousState);
        Assert.Equal(SubscriptionState.Paused, changed.NewState);
        Assert.Equal(SubscriptionLifecycleAction.Pause, changed.Action);
        Assert.Equal("Going on holiday", changed.Reason);
    }

    [Fact]
    public async Task ResumingAPausedSubscriptionCallsTheProvider()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Paused));
        _harness.BillingClient.ResumeSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Active));

        var subscription = await _harness.Service.ApplyLifecycleActionAsync(
            88001, SubscriptionLifecycleAction.Resume, null);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        await _harness.BillingClient.Received(1).ResumeSubscriptionAsync(88001, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingImmediatelyPassesTheReasonThrough()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());
        _harness.BillingClient.CancelSubscriptionAsync(88001, "Too expensive", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Canceled));

        var subscription = await _harness.Service.ApplyLifecycleActionAsync(
            88001, SubscriptionLifecycleAction.Cancel, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        await _harness.BillingClient.Received(1).CancelSubscriptionAsync(
            88001, "Too expensive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatingACancelledSubscriptionIsLegal()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Canceled));
        _harness.BillingClient.ReactivateSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Active));

        var subscription = await _harness.Service.ApplyLifecycleActionAsync(
            88001, SubscriptionLifecycleAction.Reactivate, null);

        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task LifecycleActionOnAnUnknownSubscriptionIsReportedAsNotFound()
    {
        _harness.BillingClient.FindSubscriptionAsync(999999, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<BillingProviderNotFoundException>(
            () => _harness.Service.ApplyLifecycleActionAsync(999999, SubscriptionLifecycleAction.Pause, null));
    }

    [Fact]
    public async Task ALifecycleTransitionStandsEvenWhenTheNotificationHandlerFails()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());
        _harness.BillingClient.PauseSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Paused));
        _harness.Publisher
            .Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        var subscription = await _harness.Service.ApplyLifecycleActionAsync(
            88001, SubscriptionLifecycleAction.Pause, null);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
    }

    private void StubPreviewablePlanChange()
    {
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(planHandle: "eshop-pro"));
        _harness.BillingClient.FindPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan(handle: "basic-plan", price: 29.00m));
        _harness.BillingClient.PreviewPlanChangeAsync(
                88001, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Preview());
    }
}
