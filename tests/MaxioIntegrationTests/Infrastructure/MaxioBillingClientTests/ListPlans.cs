using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class ListPlans
{
    private readonly MaxioClientBuilder _builder = new MaxioClientBuilder().WithSeededProductFamily();

    [Fact]
    public async Task ReturnsTheLivePlansOnTheConfiguredFamily()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var plans = await _builder.Build().ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Collection(plans,
            pro =>
            {
                Assert.Equal("eshop-pro", pro.Handle);
                Assert.Equal("Pro Plan", pro.Name);
                Assert.Equal(MaxioPayloads.ProId, pro.Id);
                Assert.Equal(1, pro.Interval);
                Assert.Equal("month", pro.IntervalUnit);
                Assert.False(pro.RequiresPaymentMethod);
            },
            basic => Assert.Equal("basic-plan", basic.Handle));
    }

    [Fact]
    public async Task ConvertsCentsToDollarsWithoutLosingMagnitude()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var plans = await _builder.Build().ListPlansAsync();

        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        var basic = plans.Single(plan => plan.Handle == "basic-plan");

        // The provider reports integer cents; the domain exposes dollars. Getting this wrong by a
        // factor of 100 is the classic money bug, so both representations are asserted.
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(2900, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ExcludesArchivedPlans()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var plans = await _builder.Build().ListPlansAsync();

        Assert.DoesNotContain(plans, plan => plan.Handle == "retired-plan");
    }

    [Fact]
    public async Task ReturnsEmptyWhenTheFamilyHoldsNoPlans()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK, "[]");

        var plans = await _builder.Build().ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ResolvesTheFamilyIdFromItsHandleAndCachesIt()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var client = _builder.Build();
        await client.ListPlansAsync();
        await client.ListPlansAsync();

        // Handles are durable but ids are reassigned on reseed, so the id is looked up — once.
        Assert.Single(_builder.Handler.Requests.Where(r => r.PathAndQuery == "product_families.json"));
    }

    [Fact]
    public async Task ThrowsConfigurationErrorWhenTheConfiguredFamilyHandleIsAbsent()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.OK,
            """[{"product_family":{"id":1,"name":"Something else","handle":"other-family"}}]""");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task FindPlanReturnsNullForAnUnknownHandle()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var plan = await _builder.Build().FindPlanAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Fact]
    public async Task FindPlanResolvesTheConfiguredHandle()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            $"product_families/{MaxioClientBuilder.ProductFamilyId}/products.json", HttpStatusCode.OK,
            MaxioPayloads.ProductList);

        var plan = await _builder.Build().FindPlanAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal(299.00m, plan!.Price);
    }
}
