using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class PlanChange
{
    private const string PREVIEW_PATH = "/subscriptions/15236915/migrations/preview.json";
    private const string MIGRATE_PATH = "/subscriptions/15236915/migrations.json";
    private const string UPDATE_PATH = "/subscriptions/15236915.json";

    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task PreviewsThePlanChangeInMajorUnits()
    {
        // 27000 cents of proration, 29900 of charge, 2900 due, 27000 credited.
        _builder.Stub.Respond(HttpMethod.Post, PREVIEW_PATH,
            MaxioPayloads.MigrationPreview("27000", "29900", "2900", "27000"));

        var preview = await _builder.Build().PreviewPlanChangeAsync(15236915, "eshop-pro");

        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(270.00m, preview.ProratedAdjustment);
        Assert.Equal(299.00m, preview.Charge);
        Assert.Equal(29.00m, preview.PaymentDue);
        Assert.Equal(270.00m, preview.CreditApplied);
    }

    [Fact]
    public async Task PreservesTheBillingPeriodSoTheProviderProratesTheChange()
    {
        _builder.Stub.Respond(HttpMethod.Post, PREVIEW_PATH,
            MaxioPayloads.MigrationPreview("0", "2900", "2900", "0"));

        await _builder.Build().PreviewPlanChangeAsync(15236915, "basic-plan");

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        var migration = body.RootElement.GetProperty("migration");
        Assert.Equal("basic-plan", migration.GetProperty("product_handle").GetString());
        Assert.True(migration.GetProperty("preserve_period").GetBoolean());
        Assert.False(migration.GetProperty("include_trial").GetBoolean());
        Assert.False(migration.GetProperty("include_initial_charge").GetBoolean());
    }

    [Fact]
    public async Task ReadsANegativePreviewAsACreditRatherThanACharge()
    {
        // Downgrading returns money: the adjustment is negative.
        _builder.Stub.Respond(HttpMethod.Post, PREVIEW_PATH,
            MaxioPayloads.MigrationPreview("-27000", "2900", "0", "27000"));

        var preview = await _builder.Build().PreviewPlanChangeAsync(15236915, "basic-plan");

        Assert.Equal(-270.00m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.PaymentDue);
    }

    [Fact]
    public async Task SurfacesARefusedPreviewAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, PREVIEW_PATH, HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Subscription must be active"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().PreviewPlanChangeAsync(15236915, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Subscription must be active", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task MigratesTheSubscriptionWhenTheChangeAppliesNow()
    {
        _builder.Stub.Respond(HttpMethod.Post, MIGRATE_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var changed = await _builder.Build().ChangePlanAsync(15236915, "eshop-pro", PlanChangeTiming.Immediately);

        Assert.Equal("eshop-pro", changed.PlanHandle);
        Assert.Equal(299.00m, changed.PlanPrice);
        Assert.Equal(MIGRATE_PATH, _builder.Stub.LastRequest.PathAndQuery);
        Assert.Equal(HttpMethod.Post, _builder.Stub.LastRequest.Method);
    }

    [Fact]
    public async Task SchedulesADelayedProductChangeWhenTheChangeAppliesAtRenewal()
    {
        _builder.Stub.Respond(HttpMethod.Put, UPDATE_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS, nextProductHandle: "basic-plan")));

        var changed = await _builder.Build().ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // Nothing prorates: the subscription stays on its current plan until the period boundary.
        Assert.Equal("eshop-pro", changed.PlanHandle);
        Assert.Equal("basic-plan", changed.NextPlanHandle);

        var request = _builder.Stub.LastRequest;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(UPDATE_PATH, request.PathAndQuery);

        using var body = JsonDocument.Parse(request.Body!);
        var sent = body.RootElement.GetProperty("subscription");
        Assert.Equal("basic-plan", sent.GetProperty("product_handle").GetString());
        Assert.True(sent.GetProperty("product_change_delayed").GetBoolean());
    }

    [Fact]
    public async Task SurfacesARefusedMigrationAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, MIGRATE_PATH, HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Product handle: could not be found."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ChangePlanAsync(15236915, "gone-plan", PlanChangeTiming.Immediately));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("could not be found", exception.Message);
    }
}
