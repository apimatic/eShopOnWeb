using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ChangePlan : SubscriptionServiceFixture
{
    public ChangePlan()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());
    }

    [Fact]
    public async Task PreviewsAChangeWithoutCommittingIt()
    {
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Preview());

        var preview = await Service.PreviewPlanChangeAsync(UserReference, 42, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(-241.50m, preview.ProratedAdjustment);
        await BillingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitsTheChangeWhenTheConfirmedPreviewStillPrices()
    {
        var preview = Preview();
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);
        BillingClient.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: "basic-plan", planPrice: 29.00m));

        var updated = await Service.ChangePlanAsync(UserReference, 42, "basic-plan",
            PlanChangeTiming.Immediate, preview.Signature);

        Assert.Equal("basic-plan", updated.PlanHandle);
    }

    [Fact]
    public async Task PublishesThePlanChangeWithTheProratedAmount()
    {
        var preview = Preview();
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);
        BillingClient.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: "basic-plan"));

        await Service.ChangePlanAsync(UserReference, 42, "basic-plan", PlanChangeTiming.Immediate, preview.Signature);

        await Publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(changed =>
                changed.SubscriptionId == 42
                && changed.FromPlanHandle == "eshop-pro"
                && changed.ToPlanHandle == "basic-plan"
                && changed.ProrationAmount == -241.50m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToCommitAPreviewWhoseBasisHasMoved()
    {
        // The customer confirmed one amount; re-pricing now yields another. Committing would
        // charge an amount they never saw, so the change must be refused.
        var confirmed = Preview(proratedAdjustment: -241.50m);
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Preview(proratedAdjustment: -100.00m));

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => Service.ChangePlanAsync(UserReference, 42, "basic-plan",
                PlanChangeTiming.Immediate, confirmed.Signature));

        await BillingClient.DidNotReceive().ChangePlanAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesACommitWithAForgedSignature()
    {
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Preview());

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => Service.ChangePlanAsync(UserReference, 42, "basic-plan", PlanChangeTiming.Immediate, "not-a-signature"));
    }

    [Fact]
    public async Task RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var exception = await Assert.ThrowsAsync<PlanChangeNotAllowedException>(
            () => Service.PreviewPlanChangeAsync(UserReference, 42, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", exception.Reason);
        await BillingClient.DidNotReceive().PreviewPlanChangeAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeOnACancelledSubscriptionAndDirectsToReactivation()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<PlanChangeNotAllowedException>(
            () => Service.PreviewPlanChangeAsync(UserReference, 42, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Reactivate", exception.Reason);
    }

    [Fact]
    public async Task RefusesToChangeAPlanOnSomebodyElsesSubscription()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(userReference: OtherUserReference));

        // Reported exactly as a missing subscription, so ownership cannot be probed.
        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.PreviewPlanChangeAsync(UserReference, 42, "basic-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task KeepsThePlanChangeWhenPublishingTheNotificationFails()
    {
        var preview = Preview();
        BillingClient.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);
        BillingClient.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(Subscription(planHandle: "basic-plan"));
        Publisher.Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler blew up")));

        var updated = await Service.ChangePlanAsync(UserReference, 42, "basic-plan",
            PlanChangeTiming.Immediate, preview.Signature);

        Assert.Equal("basic-plan", updated.PlanHandle);
    }
}
