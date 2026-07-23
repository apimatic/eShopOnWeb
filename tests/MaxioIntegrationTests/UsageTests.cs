using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC2 — recording metered usage and reading back the period-to-date balance.</summary>
public class UsageTests
{
    private static MeteredComponent ApiCallComponent(long? pricePerUnitInCents = 1L) =>
        new(id: 3057195,
            handle: "api-call",
            name: "API Calls",
            kind: MeteredComponent.MeteredKind,
            pricingScheme: "per_unit",
            pricePerUnitInCents: pricePerUnitInCents,
            unitName: "call");

    [Fact]
    public async Task RecordingUsageReturnsTheQuantityTheProviderAccepted()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.AcceptedUsage);

        var record = await client.RecordUsageAsync(90001, "api-call", 25m, "eShopOnWeb order 42");

        Assert.Equal(900123L, record.Id);
        Assert.Equal(25m, record.Quantity);
        Assert.Equal("eShopOnWeb order 42", record.Memo);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(90001, record.SubscriptionId);
    }

    [Fact]
    public async Task UsageIsAddressedByComponentHandleUsingTheProvidersHandlePrefix()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.AcceptedUsage);

        await client.RecordUsageAsync(90001, "api-call", 1m, null);

        var url = Uri.UnescapeDataString(handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("90001", url);
        Assert.Contains("handle:api-call", url);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task TheReportedQuantityAndMemoAreSentToTheProvider()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.AcceptedUsage);

        await client.RecordUsageAsync(90001, "api-call", 25m, "batch");

        Assert.Contains("\"quantity\":25", handler.LastRequestBody);
        Assert.Contains("\"memo\":\"batch\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task AQuantityReportedBackAsAStringIsStillReadAsANumber()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.AcceptedUsageStringQuantity);

        var record = await client.RecordUsageAsync(90001, "api-call", 7m, null);

        Assert.Equal(7m, record.Quantity);
    }

    [Fact]
    public async Task RecordingUsageWithNoComponentHandleIsAConfigurationFailure()
    {
        var (client, handler) = BillingClientFixture.Create();

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.RecordUsageAsync(90001, "", 1m, null));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AProviderRejectionOfUsageSurfacesAsATypedException()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ValidationError, HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(90001, "api-call", 1m, null));

        Assert.Equal("RecordUsage", exception.Operation);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    [Fact]
    public async Task ThePeriodToDateBalanceIsAUnitCountWithAChargeDerivedFromTheUnitPrice()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.SubscriptionComponentUsage);

        var usage = await client.GetComponentUsageAsync(90001, ApiCallComponent());

        Assert.NotNull(usage);
        Assert.Equal(25, usage!.UnitBalance);
        Assert.Equal("api-call", usage.ComponentHandle);

        // 25 units at 1 cent each is 25 cents — $0.25, not $25.00.
        Assert.Equal(1L, usage.PricePerUnitInCents);
        Assert.Equal(25L, usage.EstimatedChargeInCents);
        Assert.Equal(0.25m, usage.EstimatedCharge);
    }

    [Fact]
    public async Task AZeroBalanceEstimatesAZeroCharge()
    {
        const string zeroBalance = """
            {"component": { "id": 88, "component_id": 3057195, "component_handle": "api-call",
              "kind": "metered_component", "unit_balance": 0, "subscription_id": 90001 }}
            """;

        var (client, _) = BillingClientFixture.Create(zeroBalance);

        var usage = await client.GetComponentUsageAsync(90001, ApiCallComponent());

        Assert.NotNull(usage);
        Assert.Equal(0, usage!.UnitBalance);
        Assert.Equal(0L, usage.EstimatedChargeInCents);
        Assert.Equal(0m, usage.EstimatedCharge);
    }

    [Fact]
    public async Task AnUnknownUnitPriceLeavesTheEstimatedChargeUnavailableRatherThanZero()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.SubscriptionComponentUsage);

        var usage = await client.GetComponentUsageAsync(90001, ApiCallComponent(pricePerUnitInCents: null));

        Assert.NotNull(usage);
        Assert.Equal(25, usage!.UnitBalance);
        Assert.Null(usage.EstimatedChargeInCents);
        Assert.Null(usage.EstimatedCharge);
    }

    [Fact]
    public async Task ASubscriptionWithoutThatComponentYieldsNoUsageRatherThanAnError()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        Assert.Null(await client.GetComponentUsageAsync(90001, ApiCallComponent()));
    }

    [Fact]
    public async Task AProviderOutageReadingUsageSurfacesAsATypedException()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetComponentUsageAsync(90001, ApiCallComponent()));

        Assert.Equal("GetComponentUsage", exception.Operation);
    }
}
