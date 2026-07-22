using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.SubscriptionServiceTests;

public class ChangePlan
{
    private const string Target = MaxioClientBuilder.AlternateProductHandle;

    private readonly SubscriptionServiceBuilder _builder = new SubscriptionServiceBuilder().WithResolvablePlans();

    private static PlanChangePreview Quote(long paymentDueInCents = 28650) =>
        new(Target, PlanChangeTiming.Immediately, -1250, 29900, paymentDueInCents, 0);

    public ChangePlan()
    {
        _builder.BillingClient.GetSubscriptionAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());
        _builder.BillingClient.PreviewPlanChangeAsync(15236915, Target, PlanChangeTiming.Immediately,
                Arg.Any<CancellationToken>())
            .Returns(Quote());
        _builder.BillingClient.ChangePlanAsync(15236915, Target, PlanChangeTiming.Immediately,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().OnPlan(Target).Build());
    }

    [Fact]
    public async Task PreviewsWithoutCommittingAnything()
    {
        var preview = await _builder.Build()
            .PreviewPlanChangeAsync(15236915, Target, PlanChangeTiming.Immediately);

        Assert.Equal(28650, preview.PaymentDueInCents);
        Assert.Equal(286.50m, preview.PaymentDue);

        await _builder.BillingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitsWhenTheConfirmedQuoteStillMatchesAndPublishesTheChange()
    {
        var subscription = await _builder.Build()
            .ChangePlanAsync(15236915, Target, PlanChangeTiming.Immediately, Quote());

        Assert.Equal(Target, subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);

        await _builder.Publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(changed =>
                changed.PreviousPlanHandle == MaxioClientBuilder.DefaultProductHandle &&
                changed.Subscription.PlanHandle == Target),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToCommitWhenTheProvidersQuoteHasMoved()
    {
        // The customer confirmed $286.50, but the provider now quotes $300.00.
        var stale = Quote(paymentDueInCents: 30000);

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(() => _builder.Build()
            .ChangePlanAsync(15236915, Target, PlanChangeTiming.Immediately, stale));

        // Never charge an amount other than the one the customer was shown (UC3).
        await _builder.BillingClient.DidNotReceive().ChangePlanAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAChangeToThePlanAlreadyInForce()
    {
        var exception = await Assert.ThrowsAsync<InvalidPlanChangeException>(() => _builder.Build()
            .PreviewPlanChangeAsync(15236915, MaxioClientBuilder.DefaultProductHandle,
                PlanChangeTiming.Immediately));

        Assert.Contains("already on plan", exception.Message);

        await _builder.BillingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanChangeOnACancelledSubscription()
    {
        _builder.BillingClient.GetSubscriptionAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().InState(SubscriptionState.Canceled).Build());

        var exception = await Assert.ThrowsAsync<InvalidPlanChangeException>(() => _builder.Build()
            .PreviewPlanChangeAsync(15236915, Target, PlanChangeTiming.Immediately));

        Assert.Contains("Reactivate it first", exception.Message);
    }

    [Fact]
    public async Task RejectsAnUnresolvableTargetPlanHandle()
    {
        await Assert.ThrowsAsync<BillingConfigurationException>(() => _builder.Build()
            .PreviewPlanChangeAsync(15236915, "stale-handle", PlanChangeTiming.Immediately));
    }

    [Fact]
    public async Task RejectsAPlanChangeOnAnUnknownSubscription()
    {
        _builder.BillingClient.GetSubscriptionAsync(404404, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _builder.Build()
            .PreviewPlanChangeAsync(404404, Target, PlanChangeTiming.Immediately));
    }
}
