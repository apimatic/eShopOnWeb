using System.Net;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Catalog validation is the operator's read-back of the provider seed (UC0) and the standing
/// precondition behind usage reporting (UC2). It reports rather than throws, so a provider outage
/// during validation can never take a host down.
/// </summary>
public class CatalogValidation
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReportsAValidCatalog()
    {
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Equal(BillingClientBuilder.ProductFamilyId, validation.ProductFamilyId);
        Assert.True(validation.IsMeteredComponentValid);
        Assert.Equal(BillingClientBuilder.MeteredComponentId, validation.MeteredComponentId);
        Assert.Equal(2, validation.Plans.Count);

        // Reported as the provider's own wire value, not the SDK enum's record rendering.
        Assert.Equal("metered_component", validation.MeteredComponentKind);
    }

    [Fact]
    public async Task ReportsAMissingProductFamilyWithoutThrowing()
    {
        _handler.RespondOk(HttpMethod.Get, "/product_families.json",
            MaxioJson.ProductFamilies((1, "a-different-family")));
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.False(validation.IsValid);
        Assert.Null(validation.ProductFamilyId);
        Assert.Contains(validation.Errors, error => error.Contains(BillingClientBuilder.ProductFamilyHandle));
    }

    [Fact]
    public async Task ReportsAConfiguredPlanHandleThatDoesNotResolve()
    {
        // Only the Pro plan exists, so the configured alternate plan is missing from the seed.
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Get, "/products.json",
                MaxioJson.ProductListOf((MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", MaxioJson.ProPlanPriceInCents)));
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains(BillingClientBuilder.AlternatePlanHandle));
    }

    [Fact]
    public async Task ReportsAComponentOfTheWrongKindWithoutThrowing()
    {
        _handler.WithMeteredComponent(kind: "quantity_based_component")
            .RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.False(validation.IsValid);
        Assert.False(validation.IsMeteredComponentValid);
        Assert.Contains(validation.Errors, error => error.Contains("not metered"));
    }

    [Fact]
    public async Task ReportsAnUnreachableProviderWithoutThrowing()
    {
        _handler.Unreachable(HttpMethod.Get, "/product_families.json");
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task ReportsAMissingComponentWithoutThrowing()
    {
        _handler
            .RespondOk(HttpMethod.Get, "/product_families.json",
                MaxioJson.ProductFamilies((BillingClientBuilder.ProductFamilyId, BillingClientBuilder.ProductFamilyHandle)))
            .RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList())
            .Respond(HttpMethod.Get, "/components/handle:api-call", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        var validation = await client.ValidateCatalogAsync();

        Assert.False(validation.IsValid);
        Assert.False(validation.IsMeteredComponentValid);
    }
}
