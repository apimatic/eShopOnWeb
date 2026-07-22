using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class FindMeteredComponent
{
    private readonly MaxioClientBuilder _builder = new MaxioClientBuilder().WithSeededProductFamily();

    public FindMeteredComponent()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/components.json", HttpStatusCode.OK,
            MaxioPayloads.ComponentList);
    }

    [Fact]
    public async Task ReadsTheMeteredComponentAndItsKind()
    {
        var component = await _builder.Build().FindMeteredComponentAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(MaxioPayloads.ComponentId, component!.Id);
        Assert.Equal("API Calls", component.Name);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.True(component.IsMetered);
    }

    [Fact]
    public async Task ReadsTheUnitPriceAsDollarsNotCents()
    {
        var component = await _builder.Build().FindMeteredComponentAsync("api-call");

        // Component unit prices come back as a decimal string in currency units ("0.01"), unlike
        // product prices which are integer cents. Treating this as cents would bill 100x too little.
        Assert.Equal(0.01m, component!.UnitPrice);
    }

    [Fact]
    public async Task ReportsANonMeteredComponentAsNotMetered()
    {
        var component = await _builder.Build().FindMeteredComponentAsync("seats");

        Assert.NotNull(component);
        Assert.Equal("quantity_based_component", component!.Kind);
        Assert.False(component.IsMetered);
        Assert.Equal(12.50m, component.UnitPrice);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownHandle()
    {
        var component = await _builder.Build().FindMeteredComponentAsync("no-such-component");

        Assert.Null(component);
    }
}
