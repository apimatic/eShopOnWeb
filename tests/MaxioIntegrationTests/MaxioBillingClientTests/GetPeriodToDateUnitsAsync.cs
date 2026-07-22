using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class GetPeriodToDateUnitsAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    private static BillingComponent Metered() =>
        new(3062734, "api-call", "API Calls", BillingComponentKind.Metered, 0.01m, "eshop-subscribe");

    [Fact]
    public async Task ReturnsTheRunningUnitBalanceForTheCurrentPeriod()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionComponentResponse(42));

        var units = await BillingClientFixture.Create(_handler)
            .GetPeriodToDateUnitsAsync(90210, Metered());

        // A raw unit count, never a money amount.
        Assert.Equal(42, units);
    }

    [Fact]
    public async Task ReturnsZeroRatherThanNullWhenNothingHasBeenConsumedYet()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionComponentResponse(0));

        var units = await BillingClientFixture.Create(_handler)
            .GetPeriodToDateUnitsAsync(90210, Metered());

        Assert.Equal(0, units);
    }

    [Fact]
    public async Task ReturnsNullWhenTheProviderHasNoBalanceForTheComponent()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var units = await BillingClientFixture.Create(_handler)
            .GetPeriodToDateUnitsAsync(90210, Metered());

        Assert.Null(units);
    }

    [Fact]
    public async Task SurfacesARealFailureRatherThanReportingItAsNoBalance()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.InternalServerError, "\"boom\"");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).GetPeriodToDateUnitsAsync(90210, Metered()));

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task CombinesUnitsAndUnitPriceIntoThePeriodToDateCharge()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionComponentResponse(250));

        var units = await BillingClientFixture.Create(_handler)
            .GetPeriodToDateUnitsAsync(90210, Metered());

        var record = new UsageRecord(1, 90210, 3062734, "api-call", 1m, null, null);
        var report = new UsageReport(record, units, 0.01m);

        // 250 units at $0.01 is $2.50.
        Assert.True(report.PeriodToDateUnitsAvailable);
        Assert.Equal(2.50m, report.PeriodToDateCharge);
    }

    [Fact]
    public void ReportsTheChargeAsUnavailableWhenTheTotalCouldNotBeRead()
    {
        var record = new UsageRecord(1, 90210, 3062734, "api-call", 1m, null, null);
        var report = new UsageReport(record, null, 0.01m);

        Assert.False(report.PeriodToDateUnitsAvailable);
        Assert.Null(report.PeriodToDateCharge);
    }
}
