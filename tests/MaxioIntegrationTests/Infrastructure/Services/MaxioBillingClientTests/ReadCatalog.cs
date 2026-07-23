using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class ReadCatalog
{
    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task ResolvesAPlanFromItsDurableHandle()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/products/handle/eshop-pro.json",
            MaxioPayloads.ProductEnvelope(
                MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var plan = await _builder.Build().GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal(7126957, plan.Id);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task ReturnsNullForAPlanHandleThatDoesNotResolve()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, "/products/handle/gone-plan.json",
            HttpStatusCode.NotFound, "{}");

        Assert.Null(await _builder.Build().GetPlanByHandleAsync("gone-plan"));
    }

    [Fact]
    public async Task ResolvesAComponentFromItsDurableHandle()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/components/lookup.json?handle=api-call",
            MaxioPayloads.Component(3057195, "api-call", "metered_component", "0.01"));

        var component = await _builder.Build().GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(3057195, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.True(component.IsMetered);
    }

    [Fact]
    public async Task ReadsTheComponentUnitPriceAtItsTrueMagnitude()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/components/lookup.json?handle=api-call",
            MaxioPayloads.Component(3057195, "api-call", "metered_component", "0.01"));

        var component = await _builder.Build().GetComponentByHandleAsync("api-call");

        // The provider states component unit prices in major units already — one cent, not one dollar.
        Assert.Equal(0.01m, component!.UnitPrice);
    }

    [Fact]
    public async Task ReportsANonMeteredComponentAsNotMetered()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/components/lookup.json?handle=api-call",
            MaxioPayloads.Component(3057195, "api-call", "quantity_based_component", "0.01"));

        var component = await _builder.Build().GetComponentByHandleAsync("api-call");

        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task ReturnsNullForAComponentHandleThatDoesNotResolve()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, "/components/lookup.json?handle=missing",
            HttpStatusCode.NotFound, "{}");

        Assert.Null(await _builder.Build().GetComponentByHandleAsync("missing"));
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenReadingAComponent()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, "/components/lookup.json?handle=api-call",
            HttpStatusCode.Forbidden, MaxioPayloads.ErrorList("Insufficient permissions"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().GetComponentByHandleAsync("api-call"));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("Insufficient permissions", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task EscapesHandlesSoTheyCannotAlterTheRequestPath()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/products/handle/odd%2Fhandle.json",
            MaxioPayloads.ProductEnvelope(
                MaxioPayloads.Product(1, "odd/handle", "Odd", MaxioPayloads.BASIC_PLAN_CENTS)));

        var plan = await _builder.Build().GetPlanByHandleAsync("odd/handle");

        Assert.NotNull(plan);
        Assert.Equal("/products/handle/odd%2Fhandle.json", _builder.Stub.LastRequest.PathAndQuery);
    }
}
