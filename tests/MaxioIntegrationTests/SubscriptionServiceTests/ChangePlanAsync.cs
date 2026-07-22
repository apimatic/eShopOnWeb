using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>UC3 — plan change, with the staleness guard that protects the quoted price.</summary>
public class ChangePlanAsync
{
    private const string UserReference = "demouser@microsoft.com";
    private const int CustomerId = 90210;

    private static readonly BillingPlan ProPlan = new(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month");
    private static readonly BillingPlan BasicPlan = new(2, "basic-plan", "Basic Plan", 29.00m, 1, "month");

    private static (SubscriptionService Service, FakeBillingClient Billing, RecordingPublisher Publisher) Build(
        SubscriptionState state = SubscriptionState.Active)
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(ProPlan);
        billing.Plans.Add(BasicPlan);
        billing.Customer = new BillingCustomer(CustomerId, UserReference, UserReference);
        billing.Subscriptions.Add(new Subscription(50, UserReference, CustomerId, ProPlan, state,
            state.ToString().ToLowerInvariant()));

        var publisher = new RecordingPublisher();
        return (new SubscriptionService(billing, publisher, new RecordingLogger<SubscriptionService>()),
            billing, publisher);
    }

    [Fact]
    public async Task PreviewsTheProratedCostWithoutChangingAnything()
    {
        var (service, billing, _) = Build();

        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);

        Assert.Equal("eshop-pro", preview.CurrentPlan.Handle);
        Assert.Equal("basic-plan", preview.TargetPlan.Handle);
        Assert.Equal(15.00m, preview.NetAmount);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlan:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitsThePlanChangeWhenTheConfirmedQuoteStillHolds()
    {
        var (service, _, publisher) = Build();
        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);

        var updated = await service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.Immediate,
            preview.Fingerprint, UserReference);

        Assert.Equal("basic-plan", updated.Plan.Handle);

        var notification = publisher.Single<SubscriptionPlanChanged>();
        Assert.Equal("eshop-pro", notification.PreviousPlan.Handle);
        Assert.Equal("basic-plan", notification.NewPlan.Handle);
        Assert.Equal(15.00m, notification.NetAmount);
    }

    [Fact]
    public async Task RejectsTheCommitWhenTheTargetPlanWasRepricedSinceThePreview()
    {
        var (service, billing, _) = Build();
        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);

        // The plan is repriced between the customer confirming and the commit landing.
        billing.Plans[1] = new BillingPlan(2, "basic-plan", "Basic Plan", 49.00m, 1, "month");

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.Immediate,
                preview.Fingerprint, UserReference));

        // The customer must never be moved onto terms they were not shown.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlan:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitsDespiteTheProrationDriftingByAFewCentsSincePreview()
    {
        var (service, billing, _) = Build();
        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);

        // Proration is time-based, so the amounts always move a little before the customer confirms.
        billing.PreviewCredit = 10.03m;

        var updated = await service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.Immediate,
            preview.Fingerprint, UserReference);

        // Rejecting this would make a plan change impossible to complete in practice.
        Assert.Equal("basic-plan", updated.Plan.Handle);
    }

    [Fact]
    public async Task RejectsACommitCarryingAFabricatedFingerprint()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.Immediate,
                "not-a-real-fingerprint", UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlan:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsACommitConfirmedForADifferentTiming()
    {
        var (service, billing, _) = Build();
        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.AtNextRenewal,
                preview.Fingerprint, UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("ChangePlan:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAMoveToThePlanTheSubscriptionIsAlreadyOn()
    {
        var (service, billing, _) = Build();

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.PreviewPlanChangeAsync(50, "eshop-pro", PlanChangeTiming.Immediate, UserReference));

        // The message has to read as guidance, not as a state error — being on a plan is not a
        // reason a transition is illegal.
        Assert.Equal("This subscription is already on Pro Plan. Choose a different plan to change to.",
            exception.Message);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Preview:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAPlanChangeOnACancelledSubscription()
    {
        var (service, billing, _) = Build(SubscriptionState.Canceled);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference));

        // Direct the customer to reactivate first, and make no provider call.
        Assert.Equal(SubscriptionState.Canceled, exception.CurrentState);
        Assert.Contains("reactivate", exception.Message);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Preview:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsATargetPlanOutsideTheConfiguredProductFamily()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.PreviewPlanChangeAsync(50, "unknown-plan", PlanChangeTiming.Immediate, UserReference));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Preview:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesToChangeAnotherCustomersPlan()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate,
                "someone.else@example.com"));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("Preview:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsThePlanChangeWhenNotificationDeliveryFails()
    {
        var (service, billing, publisher) = Build();
        var preview = await service.PreviewPlanChangeAsync(50, "basic-plan", PlanChangeTiming.Immediate, UserReference);
        publisher.Failure = new InvalidOperationException("a handler blew up");

        var updated = await service.ChangePlanAsync(50, "basic-plan", PlanChangeTiming.Immediate,
            preview.Fingerprint, UserReference);

        Assert.Equal("basic-plan", updated.Plan.Handle);
        Assert.Contains(billing.Calls, c => c.StartsWith("ChangePlan:", StringComparison.Ordinal));
    }
}
