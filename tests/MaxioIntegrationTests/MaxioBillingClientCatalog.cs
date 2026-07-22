using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Catalog reads: resolving the product family by its durable handle, listing plans, and the
/// cents-to-dollars conversion every price crosses on the way into the domain.
/// </summary>
public class MaxioBillingClientCatalog
{
    private static MaxioApiStub StubWithFamily(MaxioApiStub stub) =>
        stub.Respond(HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"),
            HttpStatusCode.OK, MaxioJson.ProductFamilyList());

    [Fact]
    public async Task ListPlansConvertsPriceFromCentsToWholeCurrencyUnits()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK,
                MaxioJson.ProductList(
                    MaxioJson.Product(handle: "eshop-pro", name: "Pro Plan", priceInCents: 29_900L),
                    MaxioJson.Product(id: 7130998, handle: "basic-plan", name: "Basic Plan", priceInCents: 2_900L)));

        using var harness = new MaxioTestHarness(stub);

        var plans = await harness.Client.ListPlansAsync();

        Assert.Equal(2, plans.Count);

        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("month", pro.BillingPeriodDescription);

        var basic = Assert.Single(plans, p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansResolvesTheFamilyByHandleAndNotByAConfiguredId()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK, MaxioJson.ProductList(MaxioJson.Product()));

        // A stale id in configuration must not be used; the live id comes from the handle lookup.
        var settings = MaxioTestHarness.CreateSettings();
        settings.ProductFamilyId = 111111;

        using var harness = new MaxioTestHarness(stub, settings);

        var plans = await harness.Client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Contains(stub.Requests, r => r.Uri.AbsolutePath.Contains("3026730", StringComparison.Ordinal));
        Assert.DoesNotContain(stub.Requests, r => r.Uri.AbsolutePath.Contains("111111", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListPlansTargetsTheConfiguredBaseUrl()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK, MaxioJson.ProductList(MaxioJson.Product()));

        using var harness = new MaxioTestHarness(stub);

        await harness.Client.ListPlansAsync();

        // Proves the Maxio:BaseUrl override reaches the wire, not just the settings object.
        Assert.NotEmpty(stub.Requests);
        Assert.All(stub.Requests, r => Assert.Equal("maxio-stub.test", r.Uri.Host));
    }

    [Fact]
    public async Task ListPlansSendsTheConfiguredApiKeyAsBasicAuth()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK, MaxioJson.ProductList(MaxioJson.Product()));

        using var harness = new MaxioTestHarness(stub);

        await harness.Client.ListPlansAsync();

        var credential = Assert.IsType<string>(stub.Requests[0].AuthorizationParameter);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(credential));
        Assert.StartsWith($"{MaxioTestHarness.ApiKey}:", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedPlans()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK,
                MaxioJson.ProductList(
                    MaxioJson.Product(handle: "eshop-pro"),
                    MaxioJson.Product(id: 7130996, handle: "retired-plan", archivedAt: "2026-01-01T00:00:00-05:00")));

        using var harness = new MaxioTestHarness(stub);

        var plans = await harness.Client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task ListPlansReturnsAnEmptyListWhenTheFamilyHoldsNoProducts()
    {
        var stub = StubWithFamily(new MaxioApiStub())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                HttpStatusCode.OK, "[]");

        using var harness = new MaxioTestHarness(stub);

        Assert.Empty(await harness.Client.ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansThrowsAConfigurationErrorWhenTheFamilyHandleDoesNotResolve()
    {
        // The site exists but holds no family with the configured handle.
        var stub = new MaxioApiStub().Respond(
            HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"),
            HttpStatusCode.OK, """[ { "product_family": { "id": 1, "handle": "something-else" } } ]""");

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(() => harness.Client.ListPlansAsync());
        Assert.Contains("eshop-subscribe", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindProductFamilyReturnsNullWhenNoFamilyCarriesTheHandle()
    {
        var stub = new MaxioApiStub().Respond(
            HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"), HttpStatusCode.OK, "[]");

        using var harness = new MaxioTestHarness(stub);

        Assert.Null(await harness.Client.FindProductFamilyAsync("eshop-subscribe"));
    }

    [Fact]
    public async Task FindPlanByHandleMapsTheProviderPayload()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub());

        using var harness = new MaxioTestHarness(stub);

        var plan = await harness.Client.FindPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan!.Handle);
        Assert.Equal(7130997, plan.Id);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle);
        Assert.False(plan.IsArchived);
    }

    [Fact]
    public async Task FindPlanByHandleIgnoresASameHandlePlanOutsideTheConfiguredFamily()
    {
        // Maxio's own handle lookup is site-wide. Resolving inside the family is what stops a
        // subscription landing on a plan whose family does not carry the metered component.
        var stub = MaxioTestHarness.StubCatalog(
            new MaxioApiStub(),
            MaxioJson.Product(id: 7130998, handle: "basic-plan", name: "Basic Plan", priceInCents: 2_900L));

        using var harness = new MaxioTestHarness(stub);

        Assert.Null(await harness.Client.FindPlanByHandleAsync("eshop-pro"));
    }

    [Fact]
    public async Task FindPlanByHandleTreatsRequestCreditCardAloneAsNotRequiringAPaymentMethod()
    {
        // request_credit_card merely offers the card form; only require_credit_card gates signup.
        var stub = MaxioTestHarness.StubCatalog(
            new MaxioApiStub(),
            MaxioJson.Product(requireCreditCard: false, requestCreditCard: true));

        using var harness = new MaxioTestHarness(stub);

        var plan = await harness.Client.FindPlanByHandleAsync("eshop-pro");

        Assert.False(plan!.RequiresPaymentMethod);
    }

    [Fact]
    public async Task FindPlanByHandleReportsRequireCreditCard()
    {
        var stub = MaxioTestHarness.StubCatalog(
            new MaxioApiStub(),
            MaxioJson.Product(requireCreditCard: true, requestCreditCard: false));

        using var harness = new MaxioTestHarness(stub);

        Assert.True((await harness.Client.FindPlanByHandleAsync("eshop-pro"))!.RequiresPaymentMethod);
    }

    [Fact]
    public async Task FindPlanByHandleReturnsNullForAnUnknownHandle()
    {
        using var harness = new MaxioTestHarness(MaxioTestHarness.StubCatalog(new MaxioApiStub()));

        Assert.Null(await harness.Client.FindPlanByHandleAsync("no-such-plan"));
    }

    [Fact]
    public async Task FindPlanByHandleReturnsNullWithoutCallingTheProviderForABlankHandle()
    {
        var stub = new MaxioApiStub();
        using var harness = new MaxioTestHarness(stub);

        Assert.Null(await harness.Client.FindPlanByHandleAsync("  "));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task FindComponentByHandleMapsKindAndParsesTheDecimalUnitPrice()
    {
        var stub = new MaxioApiStub().Respond(
            HttpMethod.Get, MaxioApiStub.PathContaining("components"),
            HttpStatusCode.OK, MaxioJson.Component(unitPrice: "0.01"));

        using var harness = new MaxioTestHarness(stub);

        var component = await harness.Client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component!.IsMetered);
        Assert.Equal("metered_component", component.Kind);
        // The provider sends this as decimal text, not cents: 0.01 must not become 0.0001.
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("call", component.UnitName);
        Assert.Equal("eshop-subscribe", component.ProductFamilyHandle);
        Assert.False(component.IsArchived);
    }

    [Fact]
    public async Task FindComponentByHandleReportsANonMeteredKindAsNotMetered()
    {
        var stub = new MaxioApiStub().Respond(
            HttpMethod.Get, MaxioApiStub.PathContaining("components"),
            HttpStatusCode.OK, MaxioJson.Component(kind: "quantity_based_component"));

        using var harness = new MaxioTestHarness(stub);

        var component = await harness.Client.FindComponentByHandleAsync("api-call");

        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task FindComponentByHandleReturnsNullForAnUnknownHandle()
    {
        using var harness = new MaxioTestHarness(new MaxioApiStub());

        Assert.Null(await harness.Client.FindComponentByHandleAsync("no-such-component"));
    }
}
