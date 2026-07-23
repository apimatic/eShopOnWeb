using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>UC2 — recording metered usage and reading the running period-to-date balance.</summary>
public class MaxioBillingClientUsageTests
{
    private static string UsagePath =>
        $"/subscriptions/{MaxioPayloads.SubscriptionId}/components/{MaxioPayloads.ComponentId}/usages.json";

    private static string SubscriptionComponentPath =>
        $"/subscriptions/{MaxioPayloads.SubscriptionId}/components/{MaxioPayloads.ComponentId}.json";

    [Fact]
    public async Task RecordsUsageAgainstTheResolvedMeteredComponent()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Post, UsagePath, MaxioPayloads.Usage, HttpStatusCode.Created));

        var record = await harness.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, 5m, "nightly batch");

        Assert.Equal(9_001, record.Id);
        Assert.Equal(5m, record.Quantity);
        Assert.Equal("nightly batch", record.Memo);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(MaxioPayloads.SubscriptionId, record.SubscriptionId);

        // The component id is resolved from the configured handle, never hard-coded into the URL.
        var request = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, UsagePath));
        Assert.NotNull(request.Body);
        Assert.Contains("\"quantity\":5", request.Body);
        Assert.Contains("\"memo\":\"nightly batch\"", request.Body);
    }

    [Fact]
    public async Task ReadsBackAQuantityTheProviderEchoesAsADecimalString()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Post, UsagePath, MaxioPayloads.UsageWithStringQuantity, HttpStatusCode.Created));

        var record = await harness.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, 2.5m, null);

        Assert.Equal(2.5m, record.Quantity);
    }

    [Fact]
    public async Task SurfacesARejectedUsageReportWithTheProvidersMessages()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Post, UsagePath,
                """{"errors":["Subscription is not in a live state"]}""",
                HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("not in a live state", exception.Message);
    }

    [Fact]
    public async Task ReadsTheRunningPeriodToDateBalanceFromTheSubscriptionComponent()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, SubscriptionComponentPath, MaxioPayloads.SubscriptionComponent));

        var balance = await harness.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(42, balance);
    }

    [Fact]
    public async Task ReportsZeroWhenTheComponentHasNotYetAccruedOnThisSubscription()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, SubscriptionComponentPath, string.Empty, HttpStatusCode.NotFound));

        Assert.Equal(0, await harness.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId));
    }

    [Fact]
    public async Task SurfacesAFailedBalanceReadAsATypedExceptionRatherThanZero()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, SubscriptionComponentPath, """{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheConfiguredComponentIsMissingFromTheFamily()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/components.json", MaxioPayloads.EmptyList));

        await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1m, null));

        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, "/usages.json"));
    }
}
