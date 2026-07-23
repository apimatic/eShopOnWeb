using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.Services.SubscriptionServiceTests;

public class ChangePlan
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _subscriptionService;

    public ChangePlan()
    {
        _subscriptionService = new SubscriptionService(_billingClient, _publisher,
            Substitute.For<IAppLogger<SubscriptionService>>(),
            new SubscriptionSettings { ProductFamilyHandle = "eshop-subscribe", MeteredComponentHandle = "api-call" });

        _billingClient.GetSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active, "eshop-pro"));
        _billingClient.GetPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan("basic-plan", 29.00m));
    }

    [Fact]
    public async Task PreviewsTheProrationBeforeAnythingIsCommitted()
    {
        _billingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Preview(29.00m));

        var preview = await _subscriptionService.PreviewPlanChangeAsync(
            SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan");

        Assert.Equal(29.00m, preview.PaymentDue);
        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var exception = await Assert.ThrowsAsync<InvalidPlanChangeException>(
            () => _subscriptionService.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "eshop-pro"));

        Assert.Equal("eshop-pro", exception.PlanHandle);
        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToAPlanHandleThatDoesNotResolve()
    {
        _billingClient.GetPlanByHandleAsync("gone-plan", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _subscriptionService.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "gone-plan"));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeOnASubscriptionThatIsNotLive()
    {
        _billingClient.GetSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Canceled, "eshop-pro"));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _subscriptionService.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan"));

        Assert.Equal(SubscriptionState.Canceled, exception.CurrentState);
    }

    [Fact]
    public async Task RejectsAChangeOnASubscriptionThatDoesNotExist()
    {
        _billingClient.GetSubscriptionAsync(999999, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _subscriptionService.PreviewPlanChangeAsync(999999, "basic-plan"));
    }

    [Fact]
    public async Task CommitsTheChangeWhenAFreshPreviewStillMatchesTheAmountShown()
    {
        _billingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Preview(29.00m));
        _billingClient.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            PlanChangeTiming.Immediately, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active, "basic-plan", 29.00m));

        var changed = await _subscriptionService.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID,
            "basic-plan", PlanChangeTiming.Immediately, 29.00m);

        Assert.Equal("basic-plan", changed.PlanHandle);
        Assert.Equal(29.00m, changed.PlanPrice);
    }

    [Fact]
    public async Task RefusesToCommitWhenTheProrationMovedSinceThePreview()
    {
        _billingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Preview(35.00m));

        var exception = await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _subscriptionService.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
                PlanChangeTiming.Immediately, 29.00m));

        Assert.Equal(29.00m, exception.PreviewedPaymentDue);
        Assert.Equal(35.00m, exception.CurrentPaymentDue);

        // The customer is never charged an amount other than the one they were shown.
        await _billingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsTheStalenessCheckForAChangeThatDefersToTheNextRenewal()
    {
        // Nothing prorates at renewal, so there is no previewed amount to go stale.
        _billingClient.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            PlanChangeTiming.AtNextRenewal, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active, "eshop-pro"));

        await _subscriptionService.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            PlanChangeTiming.AtNextRenewal, 29.00m);

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnnouncesThePlanChangeCarryingTheOldAndNewPlans()
    {
        _billingClient.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            PlanChangeTiming.Immediately, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(SubscriptionState.Active, "basic-plan", 29.00m));

        await _subscriptionService.ChangePlanAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "basic-plan",
            PlanChangeTiming.Immediately, null);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(changed =>
                changed.OldPlanHandle == "eshop-pro"
                && changed.NewPlanHandle == "basic-plan"
                && changed.Timing == PlanChangeTiming.Immediately),
            Arg.Any<CancellationToken>());
    }
}
