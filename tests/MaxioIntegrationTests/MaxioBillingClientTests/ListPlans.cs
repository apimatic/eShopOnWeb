using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlans
{
    [Fact]
    public async Task ConvertsPriceFromCentsToMajorUnits()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ProPlanProductList);
        var client = BillingClientFixture.Create(handler);

        var plans = await client.ListPlansAsync();

        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal(299.00m, pro.Price);

        var basic = Assert.Single(plans, p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task MapsTheDescriptivePlanFields()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ProPlanProductList);
        var client = BillingClientFixture.Create(handler);

        var pro = Assert.Single(await client.ListPlansAsync(), p => p.Handle == "eshop-pro");

        Assert.Equal(7126957, pro.Id);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal("Everything, monthly", pro.Description);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.False(pro.IsArchived);
    }

    [Fact]
    public async Task ReportsArchivedAndPaymentMethodFlagsFromTheProvider()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ProPlanProductList);
        var client = BillingClientFixture.Create(handler);

        var basic = Assert.Single(await client.ListPlansAsync(), p => p.Handle == "basic-plan");

        Assert.True(basic.IsArchived);
        Assert.True(basic.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ReturnsEmptyListWhenTheFamilyHoldsNoPlans()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.EmptyList);
        var client = BillingClientFixture.Create(handler);

        Assert.Empty(await client.ListPlansAsync());
    }

    [Fact]
    public async Task ResolvesTheProductFamilyIdFromItsHandleWhenNoIdIsConfigured()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.ProductFamilyId = null;

        var handler = StubHttpMessageHandler.Sequence(
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.ProductFamilyList),
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.ProPlanProductList));

        var client = BillingClientFixture.Create(handler, settings);

        var plans = await client.ListPlansAsync();

        Assert.NotEmpty(plans);
        // The family had to be looked up before the plans could be listed.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("product_families", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task ThrowsConfigurationExceptionWhenTheConfiguredFamilyHandleIsUnknown()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.ProductFamilyId = null;
        settings.ProductFamilyHandle = "no-such-family";

        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ProductFamilyList);
        var client = BillingClientFixture.Create(handler, settings);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());
        Assert.Contains("no-such-family", exception.Message);
    }

    [Fact]
    public async Task SurfacesAProviderFailureAsATypedBillingProviderException()
    {
        var handler = StubHttpMessageHandler.Always("""{"error":"internal"}""",
            HttpStatusCode.InternalServerError);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(500, exception.StatusCode);
    }
}
