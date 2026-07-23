using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC2 — pay-as-you-go metering, including the metered-kind guard and the best-effort read-back
/// that must never fail a write that already succeeded.
/// </summary>
public class MaxioBillingClientUsageTests
{
    [Fact]
    public async Task GetMeteredComponentAsync_ResolvesTheConfiguredComponent()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ComponentEnvelope(MaxioJson.Component()));

        var component = await BillingClientFixture.Create(handler).GetMeteredComponentAsync();

        Assert.Equal(3062733, component.Id);
        Assert.Equal(BillingClientFixture.ComponentHandle, component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.Equal("call", component.UnitName);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ParsesTheUnitPriceAsDollars_NotCents()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ComponentEnvelope(MaxioJson.Component(unitPrice: "0.01")));

        var component = await BillingClientFixture.Create(handler).GetMeteredComponentAsync();

        // "0.01" is one cent per call. Reading it as cents would price each call at $0.0001.
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ParsesHighPrecisionUnitPrices_Invariantly()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ComponentEnvelope(MaxioJson.Component(unitPrice: "0.00000065")));

        var component = await BillingClientFixture.Create(handler).GetMeteredComponentAsync();

        Assert.Equal(0.00000065m, component.UnitPrice);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_LooksUpByBareHandle_WithoutAHandlePrefix()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ComponentEnvelope(MaxioJson.Component()));

        await BillingClientFixture.Create(handler).GetMeteredComponentAsync();

        var request = handler.LastRequest;
        Assert.Contains(BillingClientFixture.ComponentHandle, Uri.UnescapeDataString(request.Query));
        Assert.DoesNotContain("handle:", request.Query);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_Throws_WhenTheComponentDoesNotResolve()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.Errors("Not Found"), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(handler).GetMeteredComponentAsync());

        Assert.Contains(BillingClientFixture.ComponentHandle, ex.Message);
    }

    [Theory]
    [InlineData("quantity_based_component")]
    [InlineData("on_off_component")]
    [InlineData("prepaid_usage_component")]
    public async Task GetMeteredComponentAsync_Throws_WhenTheComponentIsNotMetered(string kind)
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ComponentEnvelope(MaxioJson.Component(kind: kind)));

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(handler).GetMeteredComponentAsync());

        Assert.Contains(kind, ex.Message);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesToMeter_AgainstANonMeteredComponent_AndSendsNoUsage()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component(kind: "quantity_based_component"))));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(handler).RecordUsageAsync(900001, 1, "memo"));

        // Only the component lookup — nothing was billed.
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task RecordUsageAsync_RecordsUnitsAndReadsBackTheRunningTotal()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.Usage(quantity: 3)),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 12)));

        var result = await BillingClientFixture.Create(handler).RecordUsageAsync(900001, 3, "order 42");

        Assert.Equal(555001, result.UsageId);
        Assert.Equal(900001, result.SubscriptionId);
        Assert.Equal(3062733, result.ComponentId);
        Assert.Equal(BillingClientFixture.ComponentHandle, result.ComponentHandle);
        Assert.Equal(3m, result.Quantity);
        Assert.Equal(12, result.PeriodToDateUnits);
        Assert.False(result.PeriodToDateUnavailable);

        // 12 units at $0.01 each is $0.12.
        Assert.Equal(0.12m, result.PeriodToDateCharge);
    }

    [Fact]
    public async Task RecordUsageAsync_AddressesTheComponentByPrefixedHandle_AndSendsTheQuantity()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.Usage()),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 1)));

        await BillingClientFixture.Create(handler).RecordUsageAsync(900001, 1, "order 42");

        var usagePost = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, usagePost.Method);

        // In a path slot the handle must carry the "handle:" prefix.
        Assert.Contains($"handle:{BillingClientFixture.ComponentHandle}", Uri.UnescapeDataString(usagePost.Path));

        var body = usagePost.Body!.Replace(" ", string.Empty);
        Assert.Contains("\"quantity\":1", body);
        Assert.Contains("\"memo\":\"order42\"", body);
    }

    [Fact]
    public async Task RecordUsageAsync_StillSucceeds_WhenTheReadBackFails()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.Usage()),
            new StubResponse(HttpStatusCode.InternalServerError, MaxioJson.Errors("boom")));

        var result = await BillingClientFixture.Create(handler).RecordUsageAsync(900001, 1, null);

        // The units are already billed; a failed read-back must not fail the operation, and must
        // not tempt a resend that would double-bill.
        Assert.Equal(555001, result.UsageId);
        Assert.Null(result.PeriodToDateUnits);
        Assert.True(result.PeriodToDateUnavailable);
        Assert.Null(result.PeriodToDateCharge);
    }

    [Fact]
    public async Task RecordUsageAsync_ReportsNoBalance_WhenTheSubscriptionHasNoLineItemYet()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.Usage()),
            StubResponse.NotFound());

        var result = await BillingClientFixture.Create(handler).RecordUsageAsync(900001, 1, null);

        Assert.Null(result.PeriodToDateUnits);
        Assert.True(result.PeriodToDateUnavailable);
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.UnprocessableEntity(MaxioJson.Errors("Subscription is not active.")));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).RecordUsageAsync(900001, 1, null));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Subscription is not active.", ex.Message);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsTheUnitBalance()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 47)));

        var balance = await BillingClientFixture.Create(handler).GetPeriodToDateUsageAsync(900001);

        Assert.Equal(47, balance);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsZero_ForAFreshPeriod()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 0)));

        // Zero is a real balance and must not be conflated with "unavailable".
        Assert.Equal(0, await BillingClientFixture.Create(handler).GetPeriodToDateUsageAsync(900001));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsNull_WhenTheSubscriptionHasNoLineItem()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.NotFound());

        Assert.Null(await BillingClientFixture.Create(handler).GetPeriodToDateUsageAsync(900001));
    }

    [Fact]
    public async Task MeteredComponent_IsResolvedOnce_AndReusedAcrossCalls()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.ComponentEnvelope(MaxioJson.Component())),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 1)),
            StubResponse.Ok(MaxioJson.SubscriptionComponent(unitBalance: 2)));

        var client = BillingClientFixture.Create(handler);

        Assert.Equal(1, await client.GetPeriodToDateUsageAsync(900001));
        Assert.Equal(2, await client.GetPeriodToDateUsageAsync(900001));

        // Three calls, not four: the component lookup is not repeated.
        Assert.Equal(3, handler.Requests.Count);
    }
}
