using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Reading the plan catalog: what the customer is shown, and at what price.</summary>
public class CatalogTests
{
    [Fact]
    public async Task ListPlansReportsPricesInMajorUnitsNotCents()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        var plans = await client.ListPlansAsync();

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(7126957, pro.Id);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);

        var basic = Assert.Single(plans, plan => plan.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
        Assert.True(basic.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListPlansHidesArchivedPlans()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        var plans = await client.ListPlansAsync();

        Assert.DoesNotContain(plans, plan => plan.Handle == "retired-plan");
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public async Task ListPlansResolvesTheFamilyFromItsHandleRatherThanAHardCodedIdentifier()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        await client.ListPlansAsync();

        Assert.Equal(1, provider.CallsTo("/product_families.json"));
        Assert.Equal(1, provider.CallsTo("/product_families/3023074/products.json"));
    }

    [Fact]
    public async Task AnEmptyCatalogIsAnEmptyListNotAFailure()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/products.json", "[]");
        var (client, _) = BillingClientFixture.Create(provider);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task AProductFamilyHandleThatDoesNotResolveIsAConfigurationFault()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json",
                """[{"product_family":{"id":99,"handle":"someone-elses-family"}}]""");
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindPlanReadsASinglePlanByItsHandle()
    {
        var provider = new FakeBillingProvider()
            .WithCatalog()
            .Respond(HttpMethod.Get, "/products", BillingPayloads.ProProduct);
        var (client, _) = BillingClientFixture.Create(provider);

        var plan = await client.FindPlanAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Contains(provider.Requests, request => request.Uri.PathAndQuery.Contains("eshop-pro", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindPlanReturnsNullForAnUnknownHandle()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/products.json", BillingPayloads.ProductsForFamily)
            .Respond(HttpMethod.Get, "/products", """{"errors":["Not Found"]}""", HttpStatusCode.NotFound);
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Null(await client.FindPlanAsync("no-such-plan"));
    }

    [Fact]
    public async Task FindPlanFallsBackToTheCatalogWhenTheDirectLookupIsUnknown()
    {
        var provider = new FakeBillingProvider()
            .WithCatalog()
            .Respond(HttpMethod.Get, "/products", "{}", HttpStatusCode.NotFound);
        var (client, _) = BillingClientFixture.Create(provider);

        var plan = await client.FindPlanAsync("basic-plan");

        Assert.NotNull(plan);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task FindPlanAcceptsTheProvidersNumericIdentifier()
    {
        var provider = new FakeBillingProvider()
            .WithCatalog()
            .Respond(HttpMethod.Get, "/products", BillingPayloads.ProProduct);
        var (client, _) = BillingClientFixture.Create(provider);

        var plan = await client.FindPlanAsync("7126957");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan.Handle);
    }

    [Fact]
    public async Task FindPlanTreatsABlankIdentifierAsNoPlanAndCallsNobody()
    {
        var provider = new FakeBillingProvider();
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Null(await client.FindPlanAsync("  "));
        Assert.Empty(provider.Requests);
    }
}
