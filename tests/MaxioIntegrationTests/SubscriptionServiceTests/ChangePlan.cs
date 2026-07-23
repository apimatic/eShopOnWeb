using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ChangePlan
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public ChangePlan()
    {
        _service = new SubscriptionService(_billingClient, _publisher, new NullAppLogger<SubscriptionService>());

        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns(TestData.Customer);
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.Subscription() });
        _billingClient.FindPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>()).Returns(TestData.BasicPlan);
    }

    [Fact]
    public async Task PreviewsTheProratedCostWithoutChangingAnything()
    {
        _billingClient.PreviewPlanChangeAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(TestData.Preview());

        var preview = await _service.PreviewPlanChangeAsync(
            TestData.BuyerId, TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(-29900, preview.ProratedAdjustmentInCents);
        Assert.Equal(0, preview.PaymentDueInCents);
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
    }

    [Fact]
    public async Task CommitsTheChangeWhenTheConfirmedPreviewStillHolds()
    {
        var preview = TestData.Preview();
        ArrangePreview(preview);
        _billingClient.ChangePlanAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(productHandle: "basic-plan", productName: "Basic Plan", productPriceInCents: 2900));

        var updated = await _service.ChangePlanAsync(
            TestData.BuyerId, TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, preview.Fingerprint);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Equal(29.00m, updated.Billing.ProductPrice);
    }

    /// <summary>
    /// The amount charged must never differ from the amount the customer was shown, so a preview
    /// that has since moved is rejected rather than applied.
    /// </summary>
    [Fact]
    public async Task RejectsTheCommitWhenTheProvidersFiguresMovedSinceThePreview()
    {
        var shownToCustomer = TestData.Preview(paymentDueInCents: 0);
        // The provider now wants money that the customer never confirmed.
        ArrangePreview(TestData.Preview(paymentDueInCents: 15000));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
                PlanChangeTiming.Immediate, shownToCustomer.Fingerprint));

        Assert.Contains("no longer current", exception.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
    }

    [Fact]
    public async Task RejectsACommitCarryingNoConfirmedPreview()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
                PlanChangeTiming.Immediate, "   "));

        await _billingClient.DidNotReceiveWithAnyArgs().ChangePlanAsync(default, default!, default, default);
    }

    [Fact]
    public async Task PublishesSubscriptionPlanChangedCarryingBothPlansAndTheConfirmedAmount()
    {
        var preview = TestData.Preview(paymentDueInCents: 2905);
        ArrangePreview(preview);
        _billingClient.ChangePlanAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(productHandle: "basic-plan", productName: "Basic Plan", productPriceInCents: 2900));

        await _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
            PlanChangeTiming.Immediate, preview.Fingerprint);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n =>
                n.PreviousPlanHandle == "eshop-pro" &&
                n.NewPlanHandle == "basic-plan" &&
                n.Timing == PlanChangeTiming.Immediate &&
                n.PaymentDueInCents == 2905),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A deferred change takes effect at the period boundary, not now.</summary>
    [Fact]
    public async Task ReportsADeferredChangeAsEffectiveAtTheEndOfTheCurrentPeriod()
    {
        var preview = TestData.Preview(timing: PlanChangeTiming.AtNextRenewal, proratedAdjustmentInCents: 0,
            chargeInCents: 2900, paymentDueInCents: 0, creditAppliedInCents: 0);
        _billingClient.PreviewPlanChangeAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal, Arg.Any<CancellationToken>())
            .Returns(preview);
        _billingClient.ChangePlanAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(nextProductHandle: "basic-plan"));

        await _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
            PlanChangeTiming.AtNextRenewal, preview.Fingerprint);

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n => n.EffectiveAt == TestData.PeriodEnd),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToThePlanTheSubscriptionIsAlreadyOn()
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.PreviewPlanChangeAsync(TestData.BuyerId, TestData.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", exception.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().PreviewPlanChangeAsync(default, default!, default, default);
    }

    [Fact]
    public async Task RejectsAChangeToAPlanHandleThatDoesNotResolve()
    {
        _billingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>()).Returns((BillingPlan?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            _service.PreviewPlanChangeAsync(TestData.BuyerId, TestData.SubscriptionId, "ghost-plan", PlanChangeTiming.Immediate));

        Assert.Contains("ghost-plan", exception.Message);
    }

    [Fact]
    public async Task RejectsAChangeOnASubscriptionThatIsNotActive()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.Subscription(SubscriptionState.Canceled) });

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.PreviewPlanChangeAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("reactivate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A customer must not be able to act on a subscription that is not theirs.</summary>
    [Fact]
    public async Task RejectsAChangeToASubscriptionBelongingToSomeoneElse()
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.PreviewPlanChangeAsync(TestData.BuyerId, 999999, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("does not belong to", exception.Message);
    }

    /// <summary>
    /// When the provider refuses a change the local check allowed, its state is the truth and must
    /// be surfaced so the customer sees the real conflict.
    /// </summary>
    [Fact]
    public async Task SurfacesTheProvidersCurrentStateWhenItRejectsTheCommit()
    {
        var preview = TestData.Preview();
        ArrangePreview(preview);
        _billingClient.ChangePlanAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("cannot migrate a subscription in this state", 422, new[] { "bad state" }));
        _billingClient.GetSubscriptionAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
                PlanChangeTiming.Immediate, preview.Fingerprint));

        Assert.Contains("currently Paused", exception.Message);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task KeepsThePlanChangeWhenTheNotificationHandlerFails()
    {
        var preview = TestData.Preview();
        ArrangePreview(preview);
        _billingClient.ChangePlanAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(productHandle: "basic-plan"));
        _publisher.Publish(Arg.Any<SubscriptionPlanChanged>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler failed"));

        var updated = await _service.ChangePlanAsync(TestData.BuyerId, TestData.SubscriptionId, "basic-plan",
            PlanChangeTiming.Immediate, preview.Fingerprint);

        Assert.Equal("basic-plan", updated.PlanHandle);
    }

    private void ArrangePreview(PlanChangePreview preview) =>
        _billingClient.PreviewPlanChangeAsync(TestData.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate, Arg.Any<CancellationToken>())
            .Returns(preview);
}
