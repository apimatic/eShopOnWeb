using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class ListPlans
{
    private const string FAMILY_PRODUCTS_PATH = "/product_families/handle:eshop-subscribe/products.json";

    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task ReturnsEveryPlanInTheConfiguredFamily()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, MaxioPayloads.ProductList(
            MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
            MaxioPayloads.Product(7126958, "basic-plan", "Basic Plan", MaxioPayloads.BASIC_PLAN_CENTS)));

        var plans = await _builder.Build().ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, plans.Select(plan => plan.Handle));
    }

    [Fact]
    public async Task ConvertsIntegerCentsIntoMajorUnits()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, MaxioPayloads.ProductList(
            MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
            MaxioPayloads.Product(7126958, "basic-plan", "Basic Plan", MaxioPayloads.BASIC_PLAN_CENTS)));

        var plans = await _builder.Build().ListPlansAsync();

        // 29900 cents is $299.00 — not $29,900.
        Assert.Equal(299.00m, plans.Single(plan => plan.Handle == "eshop-pro").Price);
        Assert.Equal(29.00m, plans.Single(plan => plan.Handle == "basic-plan").Price);
    }

    [Fact]
    public async Task CarriesTheBillingIntervalAndPaymentMethodRequirement()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, MaxioPayloads.ProductList(
            MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var plan = Assert.Single(await _builder.Build().ListPlansAsync());

        Assert.Equal(7126957, plan.Id);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task OmitsArchivedPlans()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, MaxioPayloads.ProductList(
            MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
            MaxioPayloads.Product(7126000, "retired-plan", "Retired Plan", "9900",
                archivedAt: "2025-01-01T00:00:00-05:00")));

        var plans = await _builder.Build().ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionWhenTheFamilyHasNoPlans()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, "[]");

        var plans = await _builder.Build().ListPlansAsync();

        Assert.NotNull(plans);
        Assert.Empty(plans);
    }

    [Fact]
    public async Task FallsBackToTheSiteWideProductListWhenNoFamilyIsConfigured()
    {
        _builder.WithoutProductFamilyHandle();
        _builder.Stub.Respond(HttpMethod.Get, "/products.json", MaxioPayloads.ProductList(
            MaxioPayloads.Product(7126957, "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var plans = await _builder.Build().ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("/products.json", _builder.Stub.LastRequest.PathAndQuery);
    }

    [Fact]
    public async Task TargetsTheSubdomainDerivedHostAndAuthenticatesWithTheApiKey()
    {
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, "[]");

        await _builder.Build().ListPlansAsync();

        var request = _builder.Stub.LastRequest;
        Assert.Equal("https://apimatic-hackathon.chargify.com", request.Authority);
        Assert.Equal("Basic", request.AuthorizationScheme);

        // Maxio Basic auth is the API key as the user with a literal "x" as the password.
        var decoded = System.Text.Encoding.ASCII.GetString(
            Convert.FromBase64String(request.AuthorizationParameter!));
        Assert.Equal($"{MaxioBillingClientBuilder.TEST_API_KEY}:x", decoded);
    }

    [Fact]
    public async Task TargetsAnExplicitBaseUrlWhenOneIsConfigured()
    {
        _builder.WithBaseUrl("http://localhost:8080");
        _builder.Stub.Respond(HttpMethod.Get, FAMILY_PRODUCTS_PATH, "[]");

        await _builder.Build().ListPlansAsync();

        Assert.Equal("http://localhost:8080", _builder.Stub.LastRequest.Authority);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionAsATypedExceptionCarryingItsMessages()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, FAMILY_PRODUCTS_PATH, HttpStatusCode.Unauthorized,
            MaxioPayloads.ErrorList("API key is invalid"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("API key is invalid", Assert.Single(exception.ProviderErrors));
        Assert.Contains("list plans", exception.Message);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderAsATypedException()
    {
        _builder.Stub.RespondWithTransportFailure(HttpMethod.Get, FAMILY_PRODUCTS_PATH,
            new HttpRequestException("No such host is known."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Null(exception.StatusCode);
        Assert.Contains("unreachable", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
