using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class RecordUsageAsyncTests
{
    [Fact]
    public async Task RecordsUsageAndReadsBackThePeriodToDateTotal()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "usage": { "id": 555555, "memo": "3 api calls", "created_at": "2026-07-20T12:00:00Z" } }"""),
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "component": { "id": 3057295, "unit_balance": 42 } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var result = await client.RecordUsageAsync(2001, 3, "3 api calls");

        Assert.Equal(555555, result.UsageId);
        Assert.Equal(3m, result.QuantityRecorded);
        Assert.True(result.PeriodToDateAvailable);
        Assert.Equal(42, result.PeriodToDateUnits);

        var usageBody = handler.RequestBodies[0];
        Assert.Contains("\"quantity\":3", usageBody);
        Assert.Contains("\"memo\":\"3 api calls\"", usageBody);
    }

    [Fact]
    public async Task ReportsUsageAsAvailableFalseWhenTheReadBackFails()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "usage": { "id": 555556, "created_at": "2026-07-20T12:00:00Z" } }"""),
            SequentialStubHandler.Empty(HttpStatusCode.InternalServerError));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var result = await client.RecordUsageAsync(2001, 5, memo: null);

        // The usage report itself must stand even though the read-back afterwards failed.
        Assert.Equal(555556, result.UsageId);
        Assert.Equal(5m, result.QuantityRecorded);
        Assert.False(result.PeriodToDateAvailable);
        Assert.Null(result.PeriodToDateUnits);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenUsageIsRejectedAndNeverAttemptsAReadBack()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Quantity is not a number"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.RecordUsageAsync(2001, 1, null));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Quantity is not a number", ex.Message);
        Assert.Single(handler.Requests);
    }
}
