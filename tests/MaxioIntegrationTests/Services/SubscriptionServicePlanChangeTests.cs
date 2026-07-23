using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Services;

/// <summary>UC3 — plan change with a proration preview that must still be current at commit time.</summary>
public class SubscriptionServicePlanChangeTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public SubscriptionServicePlanChangeTests()
    {
        _service = new SubscriptionService(_billingClient, _publisher, Substitute.For<IAppLogger<SubscriptionService>>());

        _billingClient.GetSubscriptionAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(planHandle: SubscriptionBuilder.BasicPlanHandle, planPriceInCents: 2_900));
        _billingClient.FindPlanAsync(SubscriptionBuilder.ProPlanHandle, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan());
    }

    [Fact]
    public async Task PreviewsTheProratedCostWithoutCommittingAnything()
    {
        var preview = SubscriptionBuilder.Preview();
        _billingClient.PreviewPlanChangeAsync(100, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);

        var result = await _service.PreviewPlanChangeAsync(
            100, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate);

        Assert.Equal(239.00m, result.PaymentDue);
        Assert.Equal(249.00m, result.Charge);
        Assert.Equal(10.00m, result.CreditApplied);
        Assert.Equal(299.00m, result.NewPlanPrice);
        await _billingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitsThePlanChangeWhenThePreviewIsStillCurrent()
    {
        var preview = SubscriptionBuilder.Preview();
        var upgraded = SubscriptionBuilder.Subscription(planHandle: SubscriptionBuilder.ProPlanHandle);
        _billingClient.PreviewPlanChangeAsync(100, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);
        _billingClient.ChangePlanAsync(100, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(upgraded);

        var result = await _service.ChangePlanAsync(
            100, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, preview.Token);

        Assert.Equal(SubscriptionBuilder.ProPlanHandle, result.PlanHandle);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n =>
                n.PreviousPlanHandle == SubscriptionBuilder.BasicPlanHandle &&
                n.Subscription.PlanHandle == SubscriptionBuilder.ProPlanHandle),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsTheCommitWhenTheProrationBasisMovedSinceThePreview()
    {
        var shown = SubscriptionBuilder.Preview(paymentDueInCents: 23_900);
        var repriced = SubscriptionBuilder.Preview(paymentDueInCents: 27_500);
        _billingClient.PreviewPlanChangeAsync(100, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(repriced);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(
                100, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate, shown.Token));

        Assert.Contains("no longer current", exception.Message);
        await _billingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsTheCommitWhenTheTimingDiffersFromThePreviewedTiming()
    {
        var shownForImmediate = SubscriptionBuilder.Preview(timing: PlanChangeTiming.Immediate);
        var atRenewal = SubscriptionBuilder.Preview(timing: PlanChangeTiming.AtNextRenewal, paymentDueInCents: 0);
        _billingClient.PreviewPlanChangeAsync(100, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.AtNextRenewal, Arg.Any<CancellationToken>())
            .Returns(atRenewal);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(
                100, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.AtNextRenewal, shownForImmediate.Token));

        await _billingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PreviewTokenIsStableForAnUnchangedSituationAndUniquePerAmount()
    {
        Assert.Equal(SubscriptionBuilder.Preview().Token, SubscriptionBuilder.Preview().Token);
        Assert.NotEqual(SubscriptionBuilder.Preview().Token, SubscriptionBuilder.Preview(paymentDueInCents: 1).Token);
        Assert.NotEqual(
            SubscriptionBuilder.Preview().Token,
            SubscriptionBuilder.Preview(targetPlanHandle: SubscriptionBuilder.BasicPlanHandle).Token);
    }

    [Fact]
    public async Task RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(
                100, SubscriptionBuilder.UserReference, SubscriptionBuilder.BasicPlanHandle, PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", exception.Message);
        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanChangeOnACancelledSubscription()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 101, state: SubscriptionState.Canceled, planHandle: SubscriptionBuilder.BasicPlanHandle));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(
                101, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate));

        Assert.Contains("Reactivate it first", exception.Message);
    }

    [Fact]
    public async Task RejectsAPlanChangeToAnUnresolvableTargetPlan()
    {
        _billingClient.FindPlanAsync("ghost-plan", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => _service.PreviewPlanChangeAsync(100, SubscriptionBuilder.UserReference, "ghost-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task RefusesToChangeThePlanOnSomebodyElsesSubscription()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com", planHandle: SubscriptionBuilder.BasicPlanHandle));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(
                200, SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, PlanChangeTiming.Immediate));
    }
}
