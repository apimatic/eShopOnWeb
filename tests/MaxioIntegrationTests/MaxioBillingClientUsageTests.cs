using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Pay-as-you-go usage (UC2): recording units and summing the running period-to-date total.
/// </summary>
public class MaxioBillingClientUsageTests
{
    [Fact]
    public async Task RecordUsageAsync_MapsTheAcceptedRecord()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Usage(id: 4400001, quantity: 5, memo: "eShopOnWeb order 12"));

        var record = await builder.Build().RecordUsageAsync(90001, 3057195, 5m, "eShopOnWeb order 12");

        Assert.Equal(4400001L, record.Id);
        Assert.Equal(90001, record.SubscriptionId);
        Assert.Equal(3057195, record.ComponentId);
        Assert.Equal(5m, record.Quantity);
        Assert.Equal("eShopOnWeb order 12", record.Memo);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero), record.RecordedAt);
    }

    [Fact]
    public async Task RecordUsageAsync_SendsTheQuantityAndMemoToTheProvider()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Usage());

        await builder.Build().RecordUsageAsync(90001, 3057195, 7m, "manual top-up");

        var request = Assert.Single(builder.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"quantity\":7", request.Body);
        Assert.Contains("manual top-up", request.Body);
    }

    [Fact]
    public async Task RecordUsageAsync_ReadsAQuantityTheProviderReturnsAsADecimalString()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.UsageWithStringQuantity("2.5"));

        var record = await builder.Build().RecordUsageAsync(90001, 3057195, 2.5m, "fractional");

        Assert.Equal(2.5m, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesAProviderRejection()
    {
        var builder = new BillingClientBuilder()
            .Respond(
                HttpStatusCode.UnprocessableEntity,
                MaxioResponses.ErrorList("Component is not metered."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().RecordUsageAsync(90001, 3057195, 1m, null));

        Assert.Contains("Component is not metered.", exception.ProviderErrors);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_SumsEveryRecordInThePeriod()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.UsageList(
                MaxioResponses.Usage(id: 1, quantity: 5),
                MaxioResponses.Usage(id: 2, quantity: 3),
                MaxioResponses.Usage(id: 3, quantity: 12)));

        var total = await builder.Build().GetPeriodToDateUsageAsync(
            90001,
            3057195,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(20m, total);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_AddsFractionalQuantitiesCorrectly()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.UsageList(
                MaxioResponses.Usage(id: 1, quantity: 2),
                MaxioResponses.UsageWithStringQuantity("2.5"),
                MaxioResponses.UsageWithStringQuantity("0.25")));

        var total = await builder.Build().GetPeriodToDateUsageAsync(90001, 3057195, null, null);

        Assert.Equal(4.75m, total);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsZeroWhenNothingHasBeenRecordedYet()
    {
        var builder = new BillingClientBuilder().RespondWithJson("[]");

        var total = await builder.Build().GetPeriodToDateUsageAsync(90001, 3057195, null, null);

        Assert.Equal(0m, total);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ScopesTheReadToTheCurrentBillingPeriod()
    {
        var builder = new BillingClientBuilder().RespondWithJson("[]");

        await builder.Build().GetPeriodToDateUsageAsync(
            90001,
            3057195,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        // Maxio stamps no billing period on a usage record, so the window must travel as a filter.
        var query = Uri.UnescapeDataString(builder.Handler.Requests.Single().Uri.Query);
        Assert.Contains("2026-07-01", query);
        Assert.Contains("2026-08-01", query);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_SurfacesAReadFailure()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().GetPeriodToDateUsageAsync(90001, 3057195, null, null));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }
}
