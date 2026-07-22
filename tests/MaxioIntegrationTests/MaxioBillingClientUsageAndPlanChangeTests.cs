using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Metered usage (UC2) and the plan-change preview and commit (UC3).</summary>
public class MaxioBillingClientUsageAndPlanChangeTests
{
    private const int SubscriptionId = 93462813;
    private const string UsagePath = "subscriptions/93462813/components/handle:api-call/usages.json";

    [Fact]
    public async Task RecordUsageAddressesTheComponentByHandleAndSendsQuantityAndMemo()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var client = BillingClientFixture.Create(stub);

        await client.RecordUsageAsync(SubscriptionId, "api-call", 250m, "eShop API calls");

        var body = JsonDocument.Parse(stub.LastRequest(HttpMethod.Post, UsagePath)!.Body!)
            .RootElement.GetProperty("usage");

        Assert.Equal(250m, body.GetProperty("quantity").GetDecimal());
        Assert.Equal("eShop API calls", body.GetProperty("memo").GetString());
    }

    [Fact]
    public async Task RecordUsageMapsTheAcceptedRecord()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, UsagePath, MaxioPayloads.UsageJson);
        var client = BillingClientFixture.Create(stub);

        var record = await client.RecordUsageAsync(SubscriptionId, "api-call", 250m, "eShop API calls");

        Assert.Equal(138522957L, record.Id);
        Assert.Equal(250m, record.Quantity);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(SubscriptionId, record.SubscriptionId);
    }

    [Fact]
    public async Task ListUsageReadsQuantitiesWhetherMaxioSendsThemAsNumbersOrStrings()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, UsagePath, MaxioPayloads.UsageListJson);
        var client = BillingClientFixture.Create(stub);

        var records = await client.ListUsageAsync(SubscriptionId, "api-call", null);

        Assert.Equal(2, records.Count);
        // "20.5" arrives as a string, 10 as a number — both must survive as decimals.
        Assert.Equal(20.5m, records.First().Quantity);
        Assert.Equal(10m, records.Last().Quantity);
        Assert.Equal(30.5m, records.Sum(r => r.Quantity));
    }

    [Fact]
    public async Task ListUsageFiltersFromTheGivenDateUsingTheProvidersDateFormat()
    {
        var pathWithFilter = UsagePath + "?since_date=2026-07-22";
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, pathWithFilter, MaxioPayloads.UsageListJson);
        var client = BillingClientFixture.Create(stub);

        await client.ListUsageAsync(SubscriptionId, "api-call",
            new DateTimeOffset(2026, 7, 22, 19, 7, 29, TimeSpan.FromHours(5)));

        Assert.Equal(1, stub.CallCount(HttpMethod.Get, pathWithFilter));
    }

    [Fact]
    public async Task ListUsageReturnsAnEmptyCollectionWhenNothingHasBeenReported()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, UsagePath, "[]");
        var client = BillingClientFixture.Create(stub);

        Assert.Empty(await client.ListUsageAsync(SubscriptionId, "api-call", null));
    }

    [Fact]
    public async Task ARejectedUsageReportSurfacesTheProvidersReason()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Post, UsagePath,
            HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Price point: could not be found.\"]}");
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(SubscriptionId, "api-call", 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Price point: could not be found.", exception.ProviderErrors);
    }

    [Fact]
    public async Task PreviewPlanChangeReportsTheProrationInCentsAndInDollars()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json", MaxioPayloads.MigrationPreviewJson);
        var client = BillingClientFixture.Create(stub);

        var preview = await client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan");

        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(-29900L, preview.ProratedAdjustmentInCents);
        Assert.Equal(3149L, preview.ChargeInCents);
        Assert.Equal(0L, preview.PaymentDueInCents);
        Assert.Equal(-26751L, preview.CreditAppliedInCents);

        Assert.Equal(-299.00m, preview.ProratedAdjustment);
        Assert.Equal(31.49m, preview.Charge);
        Assert.Equal(0.00m, preview.PaymentDue);
        Assert.Equal(-267.51m, preview.CreditApplied);
    }

    [Fact]
    public async Task PreviewPlanChangeAsksForAProratedQuoteThatPreservesTheBillingPeriod()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json", MaxioPayloads.SubscriptionJson())
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json", MaxioPayloads.MigrationPreviewJson);
        var client = BillingClientFixture.Create(stub);

        await client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan");

        var body = JsonDocument.Parse(stub.LastRequest(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations/preview.json")!.Body!)
            .RootElement.GetProperty("migration");

        Assert.Equal("basic-plan", body.GetProperty("product_handle").GetString());
        Assert.True(body.GetProperty("preserve_period").GetBoolean());
        Assert.False(body.GetProperty("include_trial").GetBoolean());
        Assert.False(body.GetProperty("include_initial_charge").GetBoolean());
    }

    [Fact]
    public async Task PreviewPlanChangeFailsWhenTheSubscriptionDoesNotExist()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var client = BillingClientFixture.Create(stub);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan"));
    }

    [Fact]
    public async Task AnImmediatePlanChangeIsAppliedAsAProratedMigration()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json",
            MaxioPayloads.SubscriptionJson(planHandle: "basic-plan", planName: "Basic Plan", priceInCents: 2900));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);
        Assert.Equal(1, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
        Assert.Equal(0, stub.CallCount(HttpMethod.Put, $"subscriptions/{SubscriptionId}.json"));
    }

    [Fact]
    public async Task AnAtRenewalPlanChangeIsScheduledWithoutProration()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Put, $"subscriptions/{SubscriptionId}.json",
            MaxioPayloads.SubscriptionJson(nextProductHandle: "basic-plan"));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var body = JsonDocument.Parse(stub.LastRequest(HttpMethod.Put, $"subscriptions/{SubscriptionId}.json")!.Body!)
            .RootElement.GetProperty("subscription");

        Assert.Equal("basic-plan", body.GetProperty("product_handle").GetString());
        Assert.True(body.GetProperty("product_change_delayed").GetBoolean());

        // The subscription stays on the current plan until the period rolls over.
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("basic-plan", subscription.NextPlanHandle);
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json"));
    }

    [Fact]
    public async Task ARejectedMigrationSurfacesTheProvidersReason()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Post, $"subscriptions/{SubscriptionId}/migrations.json",
            HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"This subscription is not eligible for a prorated migration\"]}");
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediately));

        Assert.Contains("This subscription is not eligible for a prorated migration", exception.ProviderErrors);
    }
}
