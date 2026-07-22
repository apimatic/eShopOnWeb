using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class PlanChangeAndLifecycle
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService Service => new(_billingClient, _publisher, _logger);

    private static PlanChangePreview Preview(int paymentDue = 25500) =>
        new("basic-plan", PlanChangeTiming.Immediately, -1500, 27000, paymentDue, 1500);

    public PlanChangeAndLifecycle()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));
        _billingClient.GetPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.BasicPlan);
        _billingClient.PreviewPlanChangeAsync(101, "basic-plan", PlanChangeTiming.Immediately,
                Arg.Any<CancellationToken>())
            .Returns(Preview());
        _billingClient.ChangePlanAsync(101, "basic-plan", Arg.Any<PlanChangeTiming>(),
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active,
                plan: SubscriptionBuilder.BasicPlan));
    }

    [Fact]
    public async Task CommitsThePlanChangeAndPublishesTheOldAndNewPlan()
    {
        var changed = await Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "basic-plan",
            PlanChangeTiming.Immediately, Preview());

        Assert.Equal("basic-plan", changed.Plan.Handle);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n =>
                n.PreviousPlanHandle == "eshop-pro" && n.NewPlanHandle == "basic-plan"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToCommitWhenTheQuotedProrationHasMoved()
    {
        // The customer confirmed $255.00 but the provider now quotes $260.00.
        var stale = Preview(paymentDue: 26000);

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "basic-plan",
                PlanChangeTiming.Immediately, stale));

        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitsWithoutRevalidationWhenNoQuoteWasConfirmed()
    {
        var changed = await Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "basic-plan",
            PlanChangeTiming.Immediately, null);

        Assert.Equal("basic-plan", changed.Plan.Handle);
        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "eshop-pro",
                PlanChangeTiming.Immediately, null));

        Assert.Contains("already on plan", exception.Message);
        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToAPlanHandleThatDoesNotResolve()
    {
        _billingClient.GetPlanByHandleAsync("gone", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "gone",
                PlanChangeTiming.Immediately, null));

        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanChangeOnACancelledSubscription()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ChangePlanAsync(101, SubscriptionBuilder.BuyerId, "basic-plan",
                PlanChangeTiming.Immediately, null));

        Assert.Equal(SubscriptionState.Canceled, exception.CurrentState);
        // The customer is told what they can do instead.
        Assert.Contains("reactivate", exception.LegalActions);
    }

    [Fact]
    public async Task RefusesToPreviewAPlanChangeForAnotherCustomersSubscription()
    {
        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.PreviewPlanChangeAsync(101, "someone.else@microsoft.com", "basic-plan",
                PlanChangeTiming.Immediately));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PausesAnActiveSubscriptionAndPublishesTheTransition()
    {
        _billingClient.PauseAsync(101, null, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Paused));

        var paused = await Service.PauseAsync(101, SubscriptionBuilder.BuyerId, null);

        Assert.Equal(SubscriptionState.Paused, paused.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.PreviousState == SubscriptionState.Active
                && n.NewState == SubscriptionState.Paused && n.Action == "pause"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToPauseASubscriptionThatIsNotRunning()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.PauseAsync(101, SubscriptionBuilder.BuyerId, null));

        await _billingClient.DidNotReceive().PauseAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToResumeASubscriptionThatIsNotPaused()
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ResumeAsync(101, SubscriptionBuilder.BuyerId));

        Assert.Equal(SubscriptionState.Active, exception.CurrentState);
        await _billingClient.DidNotReceive().ResumeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Paused));
        _billingClient.ResumeAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));

        var resumed = await Service.ResumeAsync(101, SubscriptionBuilder.BuyerId);

        Assert.Equal(SubscriptionState.Active, resumed.State);
    }

    [Fact]
    public async Task CancelsAtTheEndOfThePeriodWhenThatTimingIsRequested()
    {
        _billingClient.CancelAsync(101, CancellationTiming.EndOfPeriod, "too expensive",
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));

        await Service.CancelAsync(101, SubscriptionBuilder.BuyerId, CancellationTiming.EndOfPeriod,
            "too expensive");

        await _billingClient.Received(1).CancelAsync(101, CancellationTiming.EndOfPeriod,
            "too expensive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToCancelAnAlreadyCancelledSubscription()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.CancelAsync(101, SubscriptionBuilder.BuyerId, CancellationTiming.Immediate, null));

        await _billingClient.DidNotReceive().CancelAsync(Arg.Any<int>(), Arg.Any<CancellationTiming>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToReactivateAnActiveSubscription()
    {
        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.ReactivateAsync(101, SubscriptionBuilder.BuyerId));

        await _billingClient.DidNotReceive().ReactivateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Canceled));
        _billingClient.ReactivateAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));

        var reactivated = await Service.ReactivateAsync(101, SubscriptionBuilder.BuyerId);

        Assert.Equal(SubscriptionState.Active, reactivated.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n => n.Action == "reactivate"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsOnlyTheSubscriptionsBelongingToTheRequestedUser()
    {
        _billingClient.EnsureCustomerAsync(SubscriptionBuilder.BuyerId, Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(55, SubscriptionBuilder.BuyerId, SubscriptionBuilder.BuyerId, null, null));
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionBuilder.WithState(SubscriptionState.Active, id: 101),
                SubscriptionBuilder.WithState(SubscriptionState.Canceled, id: 99,
                    buyerId: "someone.else@microsoft.com")
            });

        var subscriptions = await Service.GetSubscriptionsForUserAsync(SubscriptionBuilder.BuyerId);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(101, subscription.Id);
    }
}
