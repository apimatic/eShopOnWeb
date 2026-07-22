using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC2's precondition: the configured component must resolve and must be of metered kind before a
/// single unit of usage is reported. Note the magnitude asymmetry here — a component's unit price is a
/// decimal string in dollars, unlike product prices which are integer cents.
/// </summary>
public class MeteredComponentTests
{
    [Fact]
    public async Task Resolves_the_metered_component_and_reads_its_unit_price_in_dollars()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call", unitPrice: "0.01"));

        var component = await BillingTestHarness.Build(server).GetMeteredComponentAsync();

        Assert.Equal(3062732, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);

        // One cent per unit, not one dollar: this field is dollars, so 0.01 must stay 0.01.
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("call", component.UnitName);
        Assert.Equal("per_unit", component.PricingScheme);
    }

    [Fact]
    public async Task Refuses_a_component_that_is_not_metered()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call", kind: "quantity_based_component"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetMeteredComponentAsync());

        Assert.Contains("quantity_based_component", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_component_that_lives_on_a_different_product_family()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call", familyHandle: "another-family"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetMeteredComponentAsync());

        Assert.Contains("another-family", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_archived_component()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call", archived: true));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetMeteredComponentAsync());
    }

    [Fact]
    public async Task Reports_an_unresolvable_component_handle_as_a_configuration_fault()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.NotFound(), HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetMeteredComponentAsync());

        Assert.Contains("api-call", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolves_the_component_once_and_reuses_it()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call"));

        var client = BillingTestHarness.Build(server);

        await client.GetMeteredComponentAsync();
        await client.GetMeteredComponentAsync();
        await client.GetMeteredComponentAsync();

        // The numeric id is needed on every usage path; resolving it per call would triple the traffic.
        Assert.Single(server.RequestsFor("components/lookup"));
    }
}
