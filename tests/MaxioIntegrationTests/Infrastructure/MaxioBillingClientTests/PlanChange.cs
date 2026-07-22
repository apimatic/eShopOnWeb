using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class PlanChange
{
    private const string PreviewPath = "subscriptions/15236915/migrations/preview.json";
    private const string MigratePath = "subscriptions/15236915/migrations.json";
    private const string UpdatePath = "subscriptions/15236915.json";

    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task ImmediatePreviewReturnsTheProviderQuoteInCents()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, PreviewPath, HttpStatusCode.OK,
            MaxioPayloads.MigrationPreview);

        var preview = await _builder.Build()
            .PreviewPlanChangeAsync(15236915, "eshop-pro", PlanChangeTiming.Immediately);

        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediately, preview.Timing);
        Assert.Equal(-1250, preview.ProratedAdjustmentInCents);
        Assert.Equal(29900, preview.ChargeInCents);
        Assert.Equal(28650, preview.PaymentDueInCents);
        Assert.Equal(0, preview.CreditAppliedInCents);
        Assert.Equal(286.50m, preview.PaymentDue);

        // Proration only makes sense against the current period, so the period must be preserved.
        Assert.Contains("\"preserve_period\":true", Assert.Single(_builder.Handler.Requests).Body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", _builder.Handler.Requests.Single().Body);
    }

    [Fact]
    public async Task AtNextRenewalPreviewQuotesTheNewPlanPriceWithNothingDueNow()
    {
        _builder.WithSeededProductFamily().Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var preview = await _builder.Build()
            .PreviewPlanChangeAsync(15236915, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // A deferred change raises no proration: the customer owes nothing today.
        Assert.Equal(0, preview.ProratedAdjustmentInCents);
        Assert.Equal(2900, preview.ChargeInCents);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(0m, preview.PaymentDue);

        // No migration is previewed against the provider for a deferred change.
        Assert.DoesNotContain(_builder.Handler.Requests, r => r.PathAndQuery.Contains("migrations"));
    }

    [Fact]
    public async Task AtNextRenewalPreviewFailsWhenTheTargetHandleDoesNotResolve()
    {
        _builder.WithSeededProductFamily().Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _builder.Build()
            .PreviewPlanChangeAsync(15236915, "no-such-plan", PlanChangeTiming.AtNextRenewal));
    }

    [Fact]
    public async Task ImmediateChangeMigratesTheSubscriptionPreservingThePeriod()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, MigratePath, HttpStatusCode.OK,
            MaxioPayloads.Subscription(planHandle: "basic-plan", planName: "Basic Plan", planPriceInCents: 2900));

        var subscription = await _builder.Build()
            .ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);

        var request = Assert.Single(_builder.Handler.Requests);
        Assert.Equal(MigratePath, request.PathAndQuery);
        Assert.Contains("\"preserve_period\":true", request.Body);
    }

    [Fact]
    public async Task AtNextRenewalChangeDefersTheProductChangeInsteadOfMigrating()
    {
        _builder.Handler.RespondWith(HttpMethod.Put, UpdatePath, HttpStatusCode.OK,
            MaxioPayloads.Subscription());

        await _builder.Build().ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var request = Assert.Single(_builder.Handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(UpdatePath, request.PathAndQuery);
        Assert.Contains("\"product_change_delayed\":true", request.Body);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
    }

    [Fact]
    public async Task SurfacesAProviderRefusalToMigrate()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, MigratePath, HttpStatusCode.UnprocessableEntity,
            """{"errors":["This subscription is not eligible for a prorated migration"]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => _builder.Build()
            .ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.Immediately));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("This subscription is not eligible for a prorated migration", exception.Errors);
    }
}
