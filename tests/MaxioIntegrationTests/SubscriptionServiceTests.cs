using System.Net;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The provider-agnostic seam driven over the real <c>MaxioBillingClient</c> and a stubbed Maxio.
/// These cover the domain rules the use cases promise: idempotent enrolment, usage validation,
/// stale-quote protection, legal lifecycle transitions, and best-effort eventing.
/// </summary>
public class SubscriptionServiceTests
{
    private const string Reference = "demouser@microsoft.com";
    private const string LookupPath = "customers/lookup.json?reference=demouser@microsoft.com";
    private const string CustomerSubscriptionsPath = "customers/14543792/subscriptions.json";
    private const string PlansPath = "product_families/handle:eshop-subscribe/products.json";
    private const string ComponentPath = "components/lookup.json?handle=api-call";
    private const string UsagePath = "subscriptions/93462813/components/handle:api-call/usages.json";
    private const int SubscriptionId = 93462813;

    private readonly IPublisher _publisher = Substitute.For<IPublisher>();

    private SubscriptionService CreateService(MaxioApiStub stub) =>
        new(BillingClientFixture.Create(stub), _publisher, Substitute.For<IAppLogger<SubscriptionService>>());

    private static MaxioApiStub StubWithCustomer() => new MaxioApiStub()
        .RespondOk(HttpMethod.Get, LookupPath, MaxioPayloads.CustomerJson)
        .RespondOk(HttpMethod.Get, "products/handle/eshop-pro.json", MaxioPayloads.ProPlanJson)
        .RespondOk(HttpMethod.Get, "products/handle/basic-plan.json", MaxioPayloads.BasicPlanJson);

    // ---------- UC1 ----------

    [Fact]
    public async Task SubscribeEnrolsTheUserAndAnnouncesTheActivation()
    {
        var stub = StubWithCustomer()
            .RespondOk(HttpMethod.Get, CustomerSubscriptionsPath, "[]")
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.SubscriptionJson());
        var service = CreateService(stub);

        var subscription = await service.SubscribeAsync(Reference, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(299.00m, subscription.PlanPrice);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.Subscription.Id == SubscriptionId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingTwiceToTheSamePlanReturnsTheExistingSubscriptionInsteadOfEnrollingAgain()
    {
        var stub = StubWithCustomer()
            .RespondOk(HttpMethod.Get, CustomerSubscriptionsPath, $"[{MaxioPayloads.SubscriptionJson()}]");
        var service = CreateService(stub);

        var subscription = await service.SubscribeAsync(Reference, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
        // The double-click must not create a second enrolment (UC1 failure scenario).
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribingToADifferentPlanWhileLiveIsRefusedAndPointsAtAPlanChange()
    {
        var stub = StubWithCustomer()
            .RespondOk(HttpMethod.Get, CustomerSubscriptionsPath, $"[{MaxioPayloads.SubscriptionJson()}]");
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<ActiveSubscriptionExistsException>(
            () => service.SubscribeAsync(Reference, "basic-plan"));

        Assert.Equal(SubscriptionId, exception.SubscriptionId);
        Assert.Equal("eshop-pro", exception.CurrentPlanHandle);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribingAfterCancellingEnrolsAgainRatherThanReturningTheDeadSubscription()
    {
        var stub = StubWithCustomer()
            .RespondOk(HttpMethod.Get, CustomerSubscriptionsPath, $"[{MaxioPayloads.SubscriptionJson(state: "canceled")}]")
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.SubscriptionJson(id: 93462900));
        var service = CreateService(stub);

        var subscription = await service.SubscribeAsync(Reference, "eshop-pro");

        Assert.Equal(93462900, subscription.Id);
        Assert.Equal(1, stub.CallCount(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribingToAnUnknownPlanFailsBeforeAnyCustomerIsTouched()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "products/handle/ghost-plan.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = CreateService(stub);

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(Reference, "ghost-plan"));

        Assert.Equal(0, stub.CallCount(HttpMethod.Get, LookupPath));
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribingToAnArchivedPlanIsRefusedAsAConfigurationProblem()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "products/handle/legacy-plan.json", MaxioPayloads.RetiredPlanJson);
        var service = CreateService(stub);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => service.SubscribeAsync(Reference, "legacy-plan"));
    }

    [Fact]
    public async Task AFailingNotificationHandlerNeverUndoesASuccessfulEnrolment()
    {
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("handler blew up")));

        var stub = StubWithCustomer()
            .RespondOk(HttpMethod.Get, CustomerSubscriptionsPath, "[]")
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.SubscriptionJson());
        var service = CreateService(stub);

        // Eventing is best-effort (plan.md §2.5): the subscription stands.
        var subscription = await service.SubscribeAsync(Reference, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
    }

    [Fact]
    public async Task AUserWithNoProviderCustomerHasNoSubscriptions()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = CreateService(stub);

        Assert.Empty(await service.GetSubscriptionsForUserAsync(Reference));
    }

    // ---------- UC2 ----------

    [Fact]
    public async Task RecordUsageReportsTheRunningPeriodToDateTotalAndTheAccruedCharge()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson)
            .RespondOk(HttpMethod.Get, UsagePath + "?since_date=2026-07-22", MaxioPayloads.UsageListJson);
        var service = CreateService(stub);

        var summary = await service.RecordUsageAsync(SubscriptionId, 250m, "eShop API calls");

        Assert.True(summary.TotalAvailable);
        Assert.Equal(30.5m, summary.PeriodToDateQuantity);
        Assert.Equal(0.01m, summary.UnitPrice);
        // 30.5 units at a cent each.
        Assert.Equal(0.305m, summary.EstimatedCharge);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 19, 7, 29, TimeSpan.FromHours(5)), summary.NextInvoiceAt);
    }

    [Fact]
    public async Task TheRunningTotalCountsOnlyUsageInsideTheCurrentBillingPeriod()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Get, UsagePath + "?since_date=2026-07-22", MaxioPayloads.UsageListSpanningPeriodsJson);
        var service = CreateService(stub);

        var summary = await service.GetUsageAsync(SubscriptionId);

        // The provider filters from midnight, so the 900 units logged before the period started
        // must be trimmed off rather than billed again.
        Assert.Equal(20.5m, summary.PeriodToDateQuantity);
        Assert.Single(summary.Records);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-250)]
    public async Task AnInvalidQuantityIsRejectedBeforeAnythingReachesTheProvider(decimal quantity)
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var service = CreateService(stub);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordUsageAsync(SubscriptionId, quantity, null));

        Assert.Equal(0, stub.CallCount(HttpMethod.Post, UsagePath));
    }

    [Fact]
    public async Task UsageIsRefusedForASubscriptionThatIsNotLive()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson(state: "canceled"))
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<IllegalSubscriptionTransitionException>(
            () => service.RecordUsageAsync(SubscriptionId, 1m, null));

        Assert.Equal("Canceled", exception.CurrentState);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, UsagePath));
    }

    [Fact]
    public async Task UsageIsRefusedWhenTheConfiguredComponentIsNotMetered()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.QuantityBasedComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.RecordUsageAsync(SubscriptionId, 1m, null));

        Assert.Contains("not metered", exception.Message);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, UsagePath));
    }

    [Fact]
    public async Task UsageIsRefusedWhenTheConfiguredComponentDoesNotExist()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .Respond(HttpMethod.Get, ComponentPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var service = CreateService(stub);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => service.RecordUsageAsync(SubscriptionId, 1m, null));

        Assert.Equal(0, stub.CallCount(HttpMethod.Post, UsagePath));
    }

    [Fact]
    public async Task UsageForAnUnknownSubscriptionIsRejected()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "subscriptions/999999999.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = CreateService(stub);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => service.RecordUsageAsync(999999999, 1m, null));
    }

    [Fact]
    public async Task AFailedReadBackLeavesTheRecordedUsageStandingButMarksTheTotalUnavailable()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson)
            .Respond(HttpMethod.Get, UsagePath + "?since_date=2026-07-22",
                HttpStatusCode.InternalServerError, "{\"errors\":[\"boom\"]}");
        var service = CreateService(stub);

        // Failing the whole call would invite a retry and double-bill the units (UC2).
        var summary = await service.RecordUsageAsync(SubscriptionId, 250m, null);

        Assert.False(summary.TotalAvailable);
        Assert.Equal(0m, summary.PeriodToDateQuantity);
        Assert.Null(summary.EstimatedCharge);
        Assert.Equal(1, stub.CallCount(HttpMethod.Post, UsagePath));
    }

    [Fact]
    public async Task TheMeteredComponentIsValidatedOnceAndRememberedAcrossCalls()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, ComponentPath, MaxioPayloads.MeteredComponentJson)
            .RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson)
            .RespondOk(HttpMethod.Get, UsagePath + "?since_date=2026-07-22", MaxioPayloads.UsageListJson);
        var service = CreateService(stub);

        await service.RecordUsageAsync(SubscriptionId, 1m, null);
        await service.RecordUsageAsync(SubscriptionId, 2m, null);

        Assert.Equal(2, stub.CallCount(HttpMethod.Post, UsagePath));
        Assert.Equal(1, stub.CallCount(HttpMethod.Get, ComponentPath));
    }

    // ---------- UC3 ----------

    [Fact]
    public async Task AnAtRenewalPreviewQuotesTheNewPlanPriceWithoutCallingTheProrationEndpoint()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Get, "products/handle/basic-plan.json", MaxioPayloads.BasicPlanJson);
        var service = CreateService(stub);

        var preview = await service.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(2900L, preview.ChargeInCents);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(0L, preview.PaymentDueInCents);
        Assert.Equal(0L, preview.ProratedAdjustmentInCents);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json"));
    }

    [Fact]
    public async Task ChangingToThePlanAlreadyHeldIsRejectedAsANoOp()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson());
        var service = CreateService(stub);

        await Assert.ThrowsAsync<PlanChangeNotApplicableException>(
            () => service.PreviewPlanChangeAsync(SubscriptionId, "eshop-pro", PlanChangeTiming.Immediately));

        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json"));
    }

    [Fact]
    public async Task APlanChangeIsRefusedWhenTheSubscriptionIsNotInAChangeableState()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson(state: "canceled"));
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<IllegalSubscriptionTransitionException>(
            () => service.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately));

        Assert.Equal("Canceled", exception.CurrentState);
        Assert.Contains(SubscriptionActions.Reactivate, exception.LegalActions);
    }

    [Fact]
    public async Task CommittingWithTheQuoteTheCustomerSawAppliesTheChangeAndAnnouncesIt()
    {
        var stub = PlanChangeStub();
        var service = CreateService(stub);

        var quoted = await service.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately);
        var subscription = await service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, quoted);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionPlanChanged>(n => n.PreviousPlanHandle == "eshop-pro" && n.Subscription.PlanHandle == "basic-plan"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommittingWithAQuoteThatNoLongerHoldsIsRefusedWithoutChangingAnything()
    {
        var stub = PlanChangeStub();
        var service = CreateService(stub);

        // The amounts the customer saw are no longer what the provider would charge.
        var stalePreview = new PlanChangePreview(SubscriptionId, "eshop-pro", "basic-plan",
            PlanChangeTiming.Immediately, -1, 1, 1, -1);

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, stalePreview));

        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
    }

    [Fact]
    public async Task CommittingWithoutAQuoteSkipsTheStalenessCheck()
    {
        var stub = PlanChangeStub();
        var service = CreateService(stub);

        var subscription = await service.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, null);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json"));
    }

    private static MaxioApiStub PlanChangeStub() => new MaxioApiStub()
        .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
        .RespondOk(HttpMethod.Get, "products/handle/basic-plan.json", MaxioPayloads.BasicPlanJson)
        .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json", MaxioPayloads.MigrationPreviewJson)
        .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json",
            MaxioPayloads.SubscriptionJson(planHandle: "basic-plan", planName: "Basic Plan", priceInCents: 2900));

    // ---------- UC4 ----------

    [Fact]
    public async Task PausingALiveSubscriptionAnnouncesTheOldAndNewState()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json", MaxioPayloads.SubscriptionJson(state: "on_hold"));
        var service = CreateService(stub);

        var subscription = await service.PauseAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.OnHold, subscription.State);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionStateChanged>(n =>
                n.PreviousState == SubscriptionState.Active &&
                n.NewState == SubscriptionState.OnHold &&
                n.Action == SubscriptionActions.Pause),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumingAnActiveSubscriptionIsRefusedWithoutCallingTheProvider()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/resume.json", MaxioPayloads.SubscriptionJson());
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<IllegalSubscriptionTransitionException>(() => service.ResumeAsync(SubscriptionId));

        Assert.Equal("Active", exception.CurrentState);
        Assert.Contains(SubscriptionActions.Pause, exception.LegalActions);
        Assert.DoesNotContain(SubscriptionActions.Resume, exception.LegalActions);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/resume.json"));
    }

    [Fact]
    public async Task ReactivatingAnActiveSubscriptionIsRefusedWithoutCallingTheProvider()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Put, $"subscriptions/{SubscriptionId}/reactivate.json", MaxioPayloads.SubscriptionJson());
        var service = CreateService(stub);

        await Assert.ThrowsAsync<IllegalSubscriptionTransitionException>(() => service.ReactivateAsync(SubscriptionId));

        Assert.Equal(0, stub.CallCount(HttpMethod.Put, $"subscriptions/{SubscriptionId}/reactivate.json"));
    }

    [Fact]
    public async Task PausingACancelledSubscriptionIsRefusedWithoutCallingTheProvider()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson(state: "canceled"))
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json", MaxioPayloads.SubscriptionJson(state: "on_hold"));
        var service = CreateService(stub);

        await Assert.ThrowsAsync<IllegalSubscriptionTransitionException>(() => service.PauseAsync(SubscriptionId));

        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json"));
    }

    [Fact]
    public async Task ReactivatingACancelledSubscriptionIsAllowed()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson(state: "canceled"))
            .RespondOk(HttpMethod.Put, $"subscriptions/{SubscriptionId}/reactivate.json", MaxioPayloads.SubscriptionJson(state: "active"));
        var service = CreateService(stub);

        Assert.Equal(SubscriptionState.Active, (await service.ReactivateAsync(SubscriptionId)).State);
    }

    [Fact]
    public async Task AnEndOfPeriodCancelKeepsTheSubscriptionLiveUntilTheBoundary()
    {
        var stub = new MaxioApiStub()
            .RespondInSequence(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
                (HttpStatusCode.OK, MaxioPayloads.SubscriptionJson()),
                (HttpStatusCode.OK, MaxioPayloads.SubscriptionJson(cancelAtEndOfPeriod: true)))
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/delayed_cancel.json", MaxioPayloads.DelayedCancelJson);
        var service = CreateService(stub);

        var subscription = await service.CancelAsync(SubscriptionId, CancellationTiming.EndOfPeriod, "switching");

        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task WhenTheProviderRefusesATransitionItsCurrentStateIsReportedAsTheTruth()
    {
        // The local view says active, but the provider has already moved it out-of-band.
        var stub = new MaxioApiStub()
            .RespondInSequence(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
                (HttpStatusCode.OK, MaxioPayloads.SubscriptionJson()),
                (HttpStatusCode.OK, MaxioPayloads.SubscriptionJson(state: "past_due")))
            .Respond(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json",
                HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorListJson);
        var service = CreateService(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => service.PauseAsync(SubscriptionId));

        Assert.Contains("PastDue", exception.Message);
        Assert.Contains("not eligible to be put on hold", exception.Message);
    }

    [Fact]
    public async Task NoNotificationIsPublishedWhenTheTransitionFails()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .Respond(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json",
                HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorListJson);
        var service = CreateService(stub);

        await Assert.ThrowsAsync<BillingProviderException>(() => service.PauseAsync(SubscriptionId));

        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALifecycleActionOnAnUnknownSubscriptionIsRejected()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "subscriptions/999999999.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = CreateService(stub);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => service.PauseAsync(999999999));
    }
}
