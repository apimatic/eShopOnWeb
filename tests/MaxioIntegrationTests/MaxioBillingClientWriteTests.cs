using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class MaxioBillingClientWriteTests
{
    [Fact]
    public async Task CreatingACustomerKeysItOnTheEShopUserReference()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.Customer);

        var customer = await builder.Build().CreateCustomerAsync("demouser@microsoft.com", "demouser@microsoft.com");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/customers.json", request.Path);

        var sent = Body(request.Body).GetProperty("customer");
        Assert.Equal("demouser@microsoft.com", sent.GetProperty("reference").GetString());
        Assert.Equal("demouser@microsoft.com", sent.GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sent.GetProperty("first_name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sent.GetProperty("last_name").GetString()));
        Assert.Equal(88833369, customer.Id);
    }

    [Fact]
    public async Task EnrollingSendsTheProductHandleAndTheCustomerId()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.Created, MaxioJson.ActiveSubscription);

        var subscription = await builder.Build().CreateSubscriptionAsync(88833369, "eshop-pro");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.Path);

        var sent = Body(request.Body).GetProperty("subscription");
        Assert.Equal("eshop-pro", sent.GetProperty("product_handle").GetString());
        Assert.Equal(88833369, sent.GetProperty("customer_id").GetInt32());
        Assert.Equal("active", subscription.State);
        Assert.Equal(299.00m, subscription.ProductPrice);
    }

    [Fact]
    public async Task UsageIsPostedAgainstTheComponentHandleOnTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.Usage);

        var usage = await builder.Build().RecordUsageAsync(15236915, "api-call", 250, "order placed");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/15236915/components/handle:api-call/usages.json", request.Path);

        var sent = Body(request.Body).GetProperty("usage");
        Assert.Equal(250, sent.GetProperty("quantity").GetDecimal());
        Assert.Equal("order placed", sent.GetProperty("memo").GetString());
        Assert.Equal(138522957, usage.Id);
        // The specification allows the quantity back as a decimal string.
        Assert.Equal(250m, usage.Quantity);
        Assert.Equal(3057195, usage.ComponentId);
    }

    [Fact]
    public async Task AProratedPlanChangePreviewPreservesThePeriodAndConvertsCents()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.MigrationPreview);

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(15236915, "basic-plan", PlanChangeTiming.ImmediateWithProration);

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal("/subscriptions/15236915/migrations/preview.json", request.Path);

        var sent = Body(request.Body).GetProperty("migration");
        Assert.Equal("basic-plan", sent.GetProperty("product_handle").GetString());
        Assert.True(sent.GetProperty("preserve_period").GetBoolean());

        Assert.Equal(-14.50m, preview.ProratedAdjustment);
        Assert.Equal(149.50m, preview.Charge);
        Assert.Equal(135.00m, preview.PaymentDue);
        Assert.True(preview.Prorate);
    }

    [Fact]
    public async Task AtRenewalTimingDoesNotPreserveThePeriod()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.MigrationPreview);

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(15236915, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var sent = Body(Assert.Single(builder.Transport.Requests).Body).GetProperty("migration");
        Assert.False(sent.GetProperty("preserve_period").GetBoolean());
        Assert.False(preview.Prorate);
    }

    [Fact]
    public async Task CommittingAPlanChangeMigratesTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        var subscription = await builder.Build()
            .ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.ImmediateWithProration);

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/15236915/migrations.json", request.Path);
        Assert.Equal(15236915, subscription.Id);
    }

    [Fact]
    public async Task PausingHoldsTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.OnHoldSubscription);

        var subscription = await builder.Build().PauseSubscriptionAsync(15236915);

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/15236915/hold.json", request.Path);
        Assert.Equal("on_hold", subscription.State);
        Assert.Equal(10.00m, subscription.Balance);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        var subscription = await builder.Build().ResumeSubscriptionAsync(15236915);

        Assert.Equal("/subscriptions/15236915/resume.json", Assert.Single(builder.Transport.Requests).Path);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task ReactivatingUsesTheReactivateEndpoint()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        await builder.Build().ReactivateSubscriptionAsync(15236915);

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/subscriptions/15236915/reactivate.json", request.Path);
    }

    [Fact]
    public async Task AnImmediateCancelDeletesTheSubscriptionAndCarriesTheReason()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        await builder.Build().CancelSubscriptionAsync(15236915, "too expensive");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/subscriptions/15236915.json", request.Path);
        Assert.Equal("too expensive",
            Body(request.Body).GetProperty("subscription").GetProperty("cancellation_message").GetString());
    }

    [Fact]
    public async Task AnEndOfPeriodCancelSchedulesThenRereadsTheProvidersView()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport
            .RespondWith(HttpStatusCode.OK, MaxioJson.DelayedCancellationAck)
            .RespondWith(HttpStatusCode.OK, MaxioJson.PendingCancellationSubscription);

        var subscription = await builder.Build().CancelSubscriptionAtEndOfPeriodAsync(15236915, "switching plans");

        Assert.Equal(2, builder.Transport.Requests.Count);
        Assert.Equal("/subscriptions/15236915/delayed_cancel.json", builder.Transport.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, builder.Transport.Requests[0].Method);
        Assert.Equal("/subscriptions/15236915.json", builder.Transport.Requests[1].Path);
        Assert.Equal(HttpMethod.Get, builder.Transport.Requests[1].Method);

        // The acknowledgement alone says nothing; the provider's own state is what is reported.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal("active", subscription.State);
        Assert.NotNull(subscription.DelayedCancelAt);
    }

    private static JsonElement Body(string? body)
    {
        Assert.False(string.IsNullOrWhiteSpace(body));
        return JsonDocument.Parse(body!).RootElement;
    }
}
