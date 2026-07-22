using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Write paths: the right Maxio operation must be invoked with the right payload, and the
/// normalized result must reflect what the provider reports back.
/// </summary>
public class MaxioBillingClientWriteTests
{
    [Fact]
    public async Task AnExistingCustomerIsReusedRatherThanCreatedAgain()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "customers/lookup.json", MaxioPayloads.Customer);

        var customer = await BillingClientFactory.Create(server).EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com");

        Assert.Equal(14714298, customer.Id);
        Assert.Empty(server.RequestsFor(HttpMethod.Post, "customers.json"));
    }

    [Fact]
    public async Task AMissingCustomerIsCreatedCarryingTheEShopUserReference()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "customers.json", HttpStatusCode.OK, MaxioPayloads.Customer);

        var customer = await BillingClientFactory.Create(server).EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com");

        Assert.Equal(14714298, customer.Id);

        var created = Assert.Single(server.RequestsFor(HttpMethod.Post, "customers.json"));
        Assert.Contains("\"reference\":\"demo@microsoft.com\"", created.Body);
        Assert.Contains("\"email\":\"demo@microsoft.com\"", created.Body);
        Assert.Contains("\"first_name\"", created.Body);
    }

    [Fact]
    public async Task ACustomerReferenceRaceResolvesToTheCustomerThatWon()
    {
        // The lookup misses, the create is rejected because the reference was taken in between,
        // and the second lookup finds the winner. Subscribing again must not fail.
        var server = new FakeMaxioServer()
            .RespondInOrder(HttpMethod.Get, "customers/lookup.json",
                (HttpStatusCode.NotFound, null),
                (HttpStatusCode.OK, MaxioPayloads.Customer))
            .Respond(HttpMethod.Post, "customers.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.CustomerReferenceTaken);

        var customer = await BillingClientFactory.Create(server).EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com");

        Assert.Equal(14714298, customer.Id);
    }

    [Fact]
    public async Task SubscribingEnrolsTheCustomerInThePlanByHandle()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.Subscription());

        var subscription = await BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(SubscriptionStates.Active, subscription.State);
        Assert.Equal(299.00m, subscription.ProductPrice);

        var created = Assert.Single(server.RequestsFor(HttpMethod.Post, "subscriptions.json"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", created.Body);
        Assert.Contains("\"customer_id\":14714298", created.Body);
    }

    [Fact]
    public async Task UsageIsPostedAgainstTheComponentHandleAndReadBackAsANumber()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions/15236915/components/handle:api-call/usages.json", MaxioPayloads.Usage);

        var receipt = await BillingClientFactory.Create(server).RecordUsageAsync(15236915, "api-call", 25m, "Order placed");

        Assert.Equal(138522957, receipt.Id);
        Assert.Equal(25m, receipt.Quantity);
        Assert.Equal(3057195, receipt.ComponentId);
        Assert.Equal("api-call", receipt.ComponentHandle);
        Assert.Equal(15236915, receipt.SubscriptionId);

        var posted = Assert.Single(server.RequestsFor(HttpMethod.Post, "usages.json"));
        Assert.Contains("\"quantity\":25", posted.Body);
        Assert.Contains("\"memo\":\"Order placed\"", posted.Body);
    }

    [Fact]
    public async Task APlanChangePreviewConvertsEveryAmountOutOfCents()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "migrations/preview.json", MaxioPayloads.MigrationPreview);

        var preview = await BillingClientFactory.Create(server).PreviewPlanChangeAsync(15236915, "eshop-pro");

        Assert.Equal(-16.67m, preview.ProratedAdjustment);
        Assert.Equal(299.00m, preview.Charge);
        Assert.Equal(282.33m, preview.PaymentDue);
        Assert.Equal(16.67m, preview.CreditApplied);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.True(preview.ApplyImmediately);
    }

    [Fact]
    public async Task PreviewingNeverMigratesTheSubscription()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "migrations/preview.json", MaxioPayloads.MigrationPreview);

        await BillingClientFactory.Create(server).PreviewPlanChangeAsync(15236915, "eshop-pro");

        Assert.Empty(server.Requests.Where(request => request.PathAndQuery.EndsWith("/migrations.json", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnImmediatePlanChangeMigratesWhilePreservingTheBillingPeriod()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions/15236915/migrations.json", MaxioPayloads.Subscription(productHandle: "basic-plan", priceInCents: 2900));

        var subscription = await BillingClientFactory.Create(server).ChangePlanAsync(15236915, "basic-plan", applyImmediately: true);

        Assert.Equal("basic-plan", subscription.ProductHandle);
        Assert.Equal(29.00m, subscription.ProductPrice);

        var migrated = Assert.Single(server.RequestsFor(HttpMethod.Post, "migrations.json"));
        Assert.Contains("\"product_handle\":\"basic-plan\"", migrated.Body);
        // Preserving the period is what makes Maxio prorate instead of restarting the period.
        Assert.Contains("\"preserve_period\":true", migrated.Body);
    }

    [Fact]
    public async Task APlanChangeAtRenewalIsScheduledRatherThanMigratedNow()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Put, "subscriptions/15236915.json", MaxioPayloads.Subscription());

        await BillingClientFactory.Create(server).ChangePlanAsync(15236915, "basic-plan", applyImmediately: false);

        Assert.Empty(server.RequestsFor(HttpMethod.Post, "migrations.json"));

        var updated = Assert.Single(server.RequestsFor(HttpMethod.Put, "subscriptions/15236915.json"));
        Assert.Contains("\"product_handle\":\"basic-plan\"", updated.Body);
        Assert.Contains("\"product_change_delayed\":true", updated.Body);
    }

    [Fact]
    public async Task PausingHoldsTheSubscription()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions/15236915/hold.json", MaxioPayloads.Subscription(state: SubscriptionStates.OnHold));

        var subscription = await BillingClientFactory.Create(server).PauseSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionStates.OnHold, subscription.State);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions/15236915/resume.json", MaxioPayloads.Subscription());

        var subscription = await BillingClientFactory.Create(server).ResumeSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionStates.Active, subscription.State);
    }

    [Fact]
    public async Task ReactivatingUsesThePutReactivateRoute()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Put, "subscriptions/15236915/reactivate.json", MaxioPayloads.Subscription());

        var subscription = await BillingClientFactory.Create(server).ReactivateSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionStates.Active, subscription.State);
        Assert.Single(server.RequestsFor(HttpMethod.Put, "reactivate.json"));
    }

    [Fact]
    public async Task AnImmediateCancellationDeletesTheSubscriptionAndCarriesTheReason()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Delete, "subscriptions/15236915.json", MaxioPayloads.Subscription(state: SubscriptionStates.Canceled));

        var subscription = await BillingClientFactory.Create(server)
            .CancelSubscriptionAsync(15236915, endOfPeriod: false, reason: "Too expensive");

        Assert.Equal(SubscriptionStates.Canceled, subscription.State);

        var deleted = Assert.Single(server.RequestsFor(HttpMethod.Delete, "subscriptions/15236915.json"));
        Assert.Contains("\"cancellation_message\":\"Too expensive\"", deleted.Body);
    }

    [Fact]
    public async Task AnEndOfPeriodCancellationDefersAndReportsTheProvidersOwnState()
    {
        // delayed_cancel only acknowledges the request, so the subscription is re-read to learn
        // the state and effective date the provider actually holds.
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions/15236915/delayed_cancel.json", MaxioPayloads.DelayedCancellation)
            .Respond(HttpMethod.Get, "subscriptions/15236915.json",
                MaxioPayloads.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: "2024-02-15T14:48:10-05:00"));

        var subscription = await BillingClientFactory.Create(server)
            .CancelSubscriptionAsync(15236915, endOfPeriod: true, reason: null);

        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2024, 2, 15, 14, 48, 10, TimeSpan.FromHours(-5)), subscription.DelayedCancelAt);
        Assert.Equal(SubscriptionStates.Active, subscription.State);
        Assert.Empty(server.RequestsFor(HttpMethod.Delete, "subscriptions/15236915.json"));
    }
}
