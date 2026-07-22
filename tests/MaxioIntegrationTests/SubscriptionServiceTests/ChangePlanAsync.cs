using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ChangePlanAsync
{
    private readonly SubscriptionServiceFixture _fixture = new();

    private static PlanChangePreview Preview(decimal proratedAdjustment) =>
        new("basic-plan", "eshop-pro", PlanChangeTiming.Immediately,
            proratedAdjustment, 270.00m, proratedAdjustment, 22.50m);

    private void ArrangeSubscriptionOnBasicPlan()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active,
                    SubscriptionServiceFixture.BasicPlan())
            });
        _fixture.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.ProPlan());
    }

    [Fact]
    public async Task CommitsTheChangeAndAnnouncesItWhenThePreviewStillMatches()
    {
        ArrangeSubscriptionOnBasicPlan();
        var preview = Preview(247.50m);
        _fixture.BillingClient.PreviewPlanChangeAsync(Arg.Any<Subscription>(), "eshop-pro",
                PlanChangeTiming.Immediately, Arg.Any<CancellationToken>())
            .Returns(preview);
        _fixture.BillingClient.ChangePlanAsync(Arg.Any<Subscription>(), "eshop-pro",
                PlanChangeTiming.Immediately, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        var updated = await _fixture.CreateService().ChangePlanAsync(SubscriptionServiceFixture.UserReference,
            "eshop-pro", PlanChangeTiming.Immediately, preview.Fingerprint);

        Assert.Equal("eshop-pro", updated.Plan.Handle);
        await _fixture.Publisher.Received(1)
            .Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToCommitWhenTheProvidersNumbersMovedSinceThePreview()
    {
        ArrangeSubscriptionOnBasicPlan();
        var shownToCustomer = Preview(247.50m);
        // The provider now prices the same change differently.
        _fixture.BillingClient.PreviewPlanChangeAsync(Arg.Any<Subscription>(), "eshop-pro",
                PlanChangeTiming.Immediately, Arg.Any<CancellationToken>())
            .Returns(Preview(300.00m));

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => _fixture.CreateService().ChangePlanAsync(SubscriptionServiceFixture.UserReference,
                "eshop-pro", PlanChangeTiming.Immediately, shownToCustomer.Fingerprint));

        // Nothing is charged at an amount the customer never saw.
        await _fixture.BillingClient.DidNotReceive().ChangePlanAsync(Arg.Any<Subscription>(),
            Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
        await _fixture.Publisher.DidNotReceive()
            .Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsMovingToThePlanTheSubscriptionIsAlreadyOn()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active) });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fixture.CreateService().PreviewPlanChangeAsync(SubscriptionServiceFixture.UserReference,
                "eshop-pro", PlanChangeTiming.Immediately));

        await _fixture.BillingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<Subscription>(),
            Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanChangeOnACancelledSubscription()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Canceled,
                    SubscriptionServiceFixture.BasicPlan())
            });

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _fixture.CreateService().PreviewPlanChangeAsync(SubscriptionServiceFixture.UserReference,
                "eshop-pro", PlanChangeTiming.Immediately));

        // The customer is told to reactivate first.
        Assert.Contains(SubscriptionLifecycleAction.Reactivate, exception.AllowedActions);
    }

    [Fact]
    public async Task RejectsATargetPlanThatDoesNotResolve()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active,
                    SubscriptionServiceFixture.BasicPlan())
            });
        _fixture.BillingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>())
            .Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.CreateService().PreviewPlanChangeAsync(SubscriptionServiceFixture.UserReference,
                "ghost-plan", PlanChangeTiming.Immediately));
    }
}
