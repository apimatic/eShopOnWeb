using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlansAsync
{
    [Fact]
    public async Task ReturnsThePlansInTheConfiguredProductFamily()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlan(), MaxioJson.BasicPlan()));

        var plans = await harness.Client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, plans.Select(p => p.Handle));
        Assert.Equal("Pro Plan", plans[0].Name);
    }

    [Fact]
    public async Task ConvertsPricesFromCentsToWholeCurrencyUnits()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlan(), MaxioJson.BasicPlan()));

        var plans = await harness.Client.ListPlansAsync();

        // 29900 cents is $299.00 — not $29,900. Getting this wrong bills a customer 100x.
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal(29.00m, plans[1].Price);
        Assert.Equal("$299.00 / month", plans[0].PriceDescription);
    }

    [Fact]
    public async Task ReportsThatNoPaymentMethodIsNeededWhenOnlyTheSignupPageAsksForACard()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlan()));

        var plans = await harness.Client.ListPlansAsync();

        // The fixture has request_credit_card true and require_credit_card false. Only the latter
        // actually refuses an enrolment, so the plan must not be reported as needing a card.
        Assert.False(plans[0].RequiresPaymentMethod);
    }

    [Fact]
    public async Task ResolvesTheProductFamilyByHandleRatherThanByAConfiguredId()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlan()));

        await harness.Client.ListPlansAsync();

        // Maxio reassigns numeric ids on a re-seed, so the family must be looked up by handle and
        // the resolved id used for the product read.
        var productRequest = harness.Handler.Requests.Single(r => r.Uri.AbsolutePath.Contains("/products.json"));
        Assert.Contains($"/product_families/{MaxioJson.ProductFamilyId}/products.json", productRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenTheFamilyHasNoProducts()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK, MaxioJson.EmptyList);

        var plans = await harness.Client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task SkipsProductsThatHaveNoHandle()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList("""{ "id": 1, "name": "Nameless", "price_in_cents": 100 }""", MaxioJson.ProPlan()));

        var plans = await harness.Client.ListPlansAsync();

        // A product with no handle could never be subscribed to reliably, so it must not be offered.
        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task ExcludesArchivedPlans()
    {
        using var harness = MaxioTestHarness.Create().WithProductFamily();
        var archived = """
            { "id": 5, "name": "Retired Plan", "handle": "retired", "price_in_cents": 100,
              "interval": 1, "interval_unit": "month", "archived_at": "2026-01-01T00:00:00-05:00" }
            """;
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK,
            MaxioJson.ProductList(archived, MaxioJson.ProPlan()));

        var plans = await harness.Client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task ThrowsAConfigurationErrorWhenTheProductFamilyHandleDoesNotResolve()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK,
            MaxioJson.ProductFamilyList(handle: "a-different-family"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.ListPlansAsync());

        // Points at the seed rather than looking like a transient outage.
        Assert.Contains("eshop-subscribe", exception.Message);
        Assert.Contains("UC0", exception.Message);
    }

    [Fact]
    public async Task ThrowsAProviderErrorCarryingTheStatusWhenCredentialsAreRejected()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/product_families.json", HttpStatusCode.Unauthorized,
            """{ "error": "Unauthorized" }""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task SendsTheApiKeyAsBasicAuthAgainstTheConfiguredHost()
    {
        using var harness = MaxioTestHarness.Create(s => s.BaseUrl = "http://localhost:8080")
            .WithProductFamily();
        harness.Handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK, MaxioJson.EmptyList);

        await harness.Client.ListPlansAsync();

        var request = harness.Handler.Requests[0];

        // The configured base URL must be honoured, not the subdomain-derived host.
        Assert.Equal("localhost", request.Uri.Host);
        Assert.Equal(8080, request.Uri.Port);

        // Maxio takes the API key as the Basic-auth username with a literal "x" password.
        var credentials = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.AuthorizationParameter!));
        Assert.Equal("test-api-key:x", credentials);
    }
}
