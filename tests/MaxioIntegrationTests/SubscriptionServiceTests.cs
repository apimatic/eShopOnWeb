using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The domain side of the provider-agnostic seam: the rules the service enforces before or around a
/// provider call, driven through the real <see cref="MaxioBillingClient"/> against the fake server so
/// the whole integration path is exercised end to end.
/// </summary>
public class SubscriptionServiceTests
{
    private const string UserReference = "demouser@microsoft.com";
    private const int SubscriptionId = 93482336;

    /// <summary>Records what was published so best-effort eventing can be asserted (§2.5).</summary>
    private class RecordingPublisher : IPublisher
    {
        public List<INotification> Published { get; } = new();
        public Exception? FailWith { get; set; }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            Published.Add(notification);

            return Task.CompletedTask;
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            Published.Add((INotification)notification);

            return Task.CompletedTask;
        }
    }

    private static (SubscriptionService Service, MaxioTestContext Context, RecordingPublisher Publisher,
        RecordingAppLogger<SubscriptionService> Logger) Build()
    {
        var context = new MaxioTestContext();
        var publisher = new RecordingPublisher();
        var logger = new RecordingAppLogger<SubscriptionService>();

        return (new SubscriptionService(context.Client, publisher, logger), context, publisher, logger);
    }

    // --- UC1 ---------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeEnrollsTheUserAndPublishesActivation()
    {
        var (service, context, publisher, _) = Build();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());
        context.Server.MapPost("customers.json", FakeResponse.Created(MaxioPayloads.Customer));
        context.Server.MapPost("subscriptions.json", FakeResponse.Created(MaxioPayloads.ActiveProSubscription));

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Equal(299.00m, subscription.PlanPrice);

        var activated = Assert.IsType<SubscriptionActivated>(Assert.Single(publisher.Published));
        Assert.Equal(SubscriptionId, activated.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingActiveSubscriptionInsteadOfEnrollingTwice()
    {
        var (service, context, publisher, _) = Build();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));
        context.Server.MapGet("customers/97865317/subscriptions.json",
            FakeResponse.Ok(MaxioPayloads.SubscriptionList));

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
        // A double-click must never create a second enrollment.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, "subscriptions.json"));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task SubscribeRefusesAPlanHandleThatDoesNotResolveAndEnrollsNothing()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(UserReference, "ghost-plan"));

        Assert.Contains("ghost-plan", exception.Message);
        Assert.Contains("UC0", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task AFailedNotificationDoesNotUndoASuccessfulEnrollment()
    {
        var (service, context, publisher, logger) = Build();
        publisher.FailWith = new InvalidOperationException("handler exploded");
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());
        context.Server.MapPost("customers.json", FakeResponse.Created(MaxioPayloads.Customer));
        context.Server.MapPost("subscriptions.json", FakeResponse.Created(MaxioPayloads.ActiveProSubscription));

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        // Eventing is best-effort: the subscription stands and the failure is only logged (§2.5).
        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Contains(logger.Warnings, w => w.Contains("SubscriptionActivated"));
    }

    // --- UC2 ---------------------------------------------------------------------------------

    [Fact]
    public async Task RecordUsageReturnsTheRecordAndTheRunningTotal()
    {
        var (service, context, _, _) = Build();
        MapUsagePath(context);

        var report = await service.RecordUsageAsync(SubscriptionId, 150, "Reported from the storefront");

        Assert.Equal(150m, report.Record.Quantity);
        Assert.True(report.IsSummaryAvailable);
        Assert.Equal(150m, report.Summary!.UnitBalance);
    }

    [Fact]
    public async Task RecordUsageStillSucceedsWhenTheRunningTotalCannotBeReadBack()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet(MaxioTestContext.ComponentRoute, FakeResponse.Ok(MaxioPayloads.MeteredComponent));
        context.Server.MapPost($"subscriptions/{SubscriptionId}/components/handle:api-call/usages.json",
            FakeResponse.Ok(MaxioPayloads.UsageRecorded));
        context.Server.MapGet($"subscriptions/{SubscriptionId}/components/handle:api-call.json",
            new FakeResponse(System.Net.HttpStatusCode.InternalServerError, """{"errors":["boom"]}"""));

        var report = await service.RecordUsageAsync(SubscriptionId, 150, null);

        // The units were accepted; only the read-back failed, so the operation must not fail.
        Assert.Equal(150m, report.Record.Quantity);
        Assert.False(report.IsSummaryAvailable);
        Assert.Null(report.Summary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordUsageRejectsANonPositiveQuantityBeforeAnyProviderCall(decimal quantity)
    {
        var (service, context, _, _) = Build();

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.RecordUsageAsync(SubscriptionId, quantity, null));

        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public async Task RecordUsageIsRejectedWhenTheSubscriptionIsNotActive()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.CanceledSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.RecordUsageAsync(SubscriptionId, 10, null));

        Assert.Contains("Canceled", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post,
            $"subscriptions/{SubscriptionId}/components/handle:api-call/usages.json"));
    }

    [Fact]
    public async Task RecordUsageForAUserWithoutAnActiveSubscriptionRecordsNothing()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());

        Assert.Null(await service.RecordUsageForUserAsync(UserReference, 1, "Order 1"));
    }

    [Fact]
    public async Task RecordUsageForAUserFindsTheirActiveSubscription()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));
        context.Server.MapGet("customers/97865317/subscriptions.json",
            FakeResponse.Ok(MaxioPayloads.SubscriptionList));
        MapUsagePath(context);

        var report = await service.RecordUsageForUserAsync(UserReference, 1, "Order 42");

        Assert.NotNull(report);
        Assert.Equal(SubscriptionId, report!.Record.SubscriptionId);
    }

    // --- UC3 ---------------------------------------------------------------------------------

    [Fact]
    public async Task ChangingToThePlanAlreadyInUseIsRejectedAsANoOp()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ChangePlanAsync(SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("already on plan", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
    }

    [Fact]
    public async Task ACancelledSubscriptionCannotChangePlan()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.CanceledSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Reactivate it first", exception.Message);
    }

    [Fact]
    public async Task PreviewingAPlanChangeReturnsTheProratedCostWithoutApplyingIt()
    {
        var (service, context, publisher, _) = Build();
        MapPlanChangePath(context);

        var preview = await service.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(-268.51m, preview.NetAmount);
        // A preview must not change anything, nor announce anything.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task PreviewingAChangeToTheCurrentPlanIsRejectedBeforePricingIt()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.PreviewPlanChangeAsync(SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post,
            $"subscriptions/{SubscriptionId}/migrations/preview.json"));
    }

    [Fact]
    public async Task PreviewingAChangeToAPlanThatDoesNotResolveIsAConfigurationError()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.PreviewPlanChangeAsync(SubscriptionId, "ghost-plan", PlanChangeTiming.Immediate));

        Assert.Contains("UC0", exception.Message);
    }

    [Fact]
    public async Task ListSubscriptionsSurfacesTheUsersEnrollmentsThroughTheService()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));
        context.Server.MapGet("customers/97865317/subscriptions.json",
            FakeResponse.Ok(MaxioPayloads.SubscriptionList));

        var subscriptions = await service.ListSubscriptionsAsync(UserReference);

        Assert.Equal(299.00m, Assert.Single(subscriptions).PlanPrice);
    }

    [Fact]
    public async Task ListPlansSurfacesTheCatalogThroughTheService()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(299.00m, plans.Single(p => p.Handle == "eshop-pro").Price);
    }

    [Fact]
    public async Task CommittingWithTheAmountFromThePreviewSucceedsAndAnnouncesTheOldPlan()
    {
        var (service, context, publisher, _) = Build();
        MapPlanChangePath(context);

        var subscription = await service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate,
            expectedNetAmount: -268.51m);

        Assert.Equal("basic-plan", subscription.PlanHandle);

        var changed = Assert.IsType<SubscriptionPlanChanged>(Assert.Single(publisher.Published));
        Assert.Equal("eshop-pro", changed.PreviousPlanHandle);
        Assert.Equal("basic-plan", changed.Subscription.PlanHandle);
    }

    [Fact]
    public async Task AStalePreviewIsRejectedRatherThanChargingADifferentAmount()
    {
        var (service, context, publisher, _) = Build();
        MapPlanChangePath(context);

        // The customer was shown -100.00 but the change now nets -268.51.
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate,
                expectedNetAmount: -100.00m));

        Assert.Contains("stale", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
        Assert.Empty(publisher.Published);
    }

    // --- UC4 ---------------------------------------------------------------------------------

    [Fact]
    public async Task PausePublishesTheOldAndNewState()
    {
        var (service, context, publisher, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));
        context.Server.MapPost($"subscriptions/{SubscriptionId}/hold.json", FakeResponse.Ok(MaxioPayloads.OnHoldSubscription));

        await service.PauseAsync(SubscriptionId);

        var changed = Assert.IsType<SubscriptionStateChanged>(Assert.Single(publisher.Published));
        Assert.Equal(SubscriptionState.Active, changed.PreviousState);
        Assert.Equal(SubscriptionState.OnHold, changed.NewState);
    }

    [Fact]
    public async Task ResumingASubscriptionThatIsNotPausedIsRejectedWithoutCallingTheProvider()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ResumeAsync(SubscriptionId));

        Assert.Contains("only a paused subscription can be resumed", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, $"subscriptions/{SubscriptionId}/resume.json"));
    }

    [Fact]
    public async Task ReactivatingAnAlreadyActiveSubscriptionIsRejected()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.ReactivateAsync(SubscriptionId));

        Assert.Contains("already active", exception.Message);
    }

    [Fact]
    public async Task PausingACancelledSubscriptionIsRejected()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.CanceledSubscription));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() => service.PauseAsync(SubscriptionId));
    }

    [Fact]
    public async Task CancellingAnAlreadyCancelledSubscriptionIsRejected()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.CanceledSubscription));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.CancelAsync(SubscriptionId, CancellationTiming.Immediate, null));

        Assert.Contains("already cancelled", exception.Message);
    }

    [Fact]
    public async Task AnUnknownSubscriptionIsRejectedRatherThanFailingInsideTheProvider()
    {
        var (service, context, _, _) = Build();
        context.Server.MapGet("subscriptions/424242.json", FakeResponse.NotFound());

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => service.PauseAsync(424242));

        Assert.Contains("was not found", exception.Message);
    }

    private static void MapUsagePath(MaxioTestContext context)
    {
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet(MaxioTestContext.ComponentRoute, FakeResponse.Ok(MaxioPayloads.MeteredComponent));
        context.Server.MapPost($"subscriptions/{SubscriptionId}/components/handle:api-call/usages.json",
            FakeResponse.Ok(MaxioPayloads.UsageRecorded));
        context.Server.MapGet($"subscriptions/{SubscriptionId}/components/handle:api-call.json",
            FakeResponse.Ok(MaxioPayloads.SubscriptionComponentWithBalance));
    }

    private static void MapPlanChangePath(MaxioTestContext context)
    {
        context.Server.MapGet($"subscriptions/{SubscriptionId}.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));
        context.Server.MapPost($"subscriptions/{SubscriptionId}/migrations/preview.json",
            FakeResponse.Ok(MaxioPayloads.MigrationPreview));
        context.Server.MapPost($"subscriptions/{SubscriptionId}/migrations.json",
            FakeResponse.Ok(MaxioPayloads.ActiveBasicSubscription));
    }
}
