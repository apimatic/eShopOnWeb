using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// UC2 must refuse to record usage unless the configured handle really is a metered component on
/// the configured family. Every rejection here is a seed problem to correct (UC0), never a retry.
/// </summary>
public class GetMeteredComponentAsync
{
    [Fact]
    public async Task ReturnsTheConfiguredMeteredComponent()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponent());

        var component = await harness.Client.GetMeteredComponentAsync();

        Assert.Equal("api-call", component.Handle);
        Assert.Equal(MaxioJson.ComponentId, component.Id);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);
    }

    [Fact]
    public async Task ReadsTheUnitPriceAsWholeCurrencyUnits()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponent(unitPrice: "0.01"));

        var component = await harness.Client.GetMeteredComponentAsync();

        // Maxio reports a component's unit price in dollars, unlike product prices which are cents.
        // Reading "0.01" as a cent value would bill a hundredth of what it should.
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task ParsesTheUnitPriceInvariantlyRegardlessOfServerCulture()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponent(unitPrice: "1234.56"));

        var component = await harness.Client.GetMeteredComponentAsync();

        // A culture that reads '.' as a group separator would turn this into 123456.
        Assert.Equal(1234.56m, component.UnitPrice);
    }

    [Fact]
    public async Task FallsBackToTheCentsFieldWhenNoDollarPriceIsReported()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponentPricedInCentsOnly(pricePerUnitInCents: 250));

        var component = await harness.Client.GetMeteredComponentAsync();

        // 250 cents is $2.50 — the fallback must convert, not copy.
        Assert.Equal(2.50m, component.UnitPrice);
    }

    [Fact]
    public async Task ThrowsAConfigurationErrorWhenTheComponentDoesNotExist()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.NotFound,
            """{ "errors": ["Component not found"] }""");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.GetMeteredComponentAsync());

        Assert.Contains("api-call", exception.Message);
    }

    [Fact]
    public async Task RefusesAComponentOfTheWrongKind()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponent(kind: "quantity_based_component"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.GetMeteredComponentAsync());

        // The remedy is specific: a kind cannot be changed in place, so say so.
        Assert.Contains("quantity_based_component", exception.Message);
        Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesAComponentThatLivesOnADifferentProductFamily()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK,
            MaxioJson.MeteredComponent(familyHandle: "some-other-family"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.GetMeteredComponentAsync());

        // A component on the wrong family is simply not available to these subscriptions.
        Assert.Contains("some-other-family", exception.Message);
    }

    [Fact]
    public async Task RefusesAnArchivedComponent()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/components/lookup.json", HttpStatusCode.OK, """
            {
              "component": {
                "id": 1, "name": "API Calls", "handle": "api-call",
                "kind": "metered_component", "unit_price": "0.01",
                "product_family_handle": "eshop-subscribe",
                "archived_at": "2026-01-01T00:00:00-05:00"
              }
            }
            """);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.GetMeteredComponentAsync());

        Assert.Contains("archived", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThrowsAConfigurationErrorWhenNoComponentHandleIsConfigured()
    {
        using var harness = MaxioTestHarness.Create(s => s.MeteredComponentHandle = "api-call");
        harness.Settings.MeteredComponentHandle = null;

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.GetMeteredComponentAsync());

        Assert.Contains("Maxio:MeteredComponentHandle", exception.Message);
        // Nothing should have gone out over the wire.
        Assert.Empty(harness.Handler.Requests);
    }
}
