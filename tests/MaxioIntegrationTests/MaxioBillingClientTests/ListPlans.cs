using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlans
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReturnsPlansCheapestFirst()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal("eshop-pro", plans[1].Handle);
    }

    [Fact]
    public async Task ConvertsProviderCentsToWholeCurrencyUnits()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        var plans = await client.ListPlansAsync();

        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        var basic = plans.Single(plan => plan.Handle == "basic-plan");

        // The provider sends 29900 cents; a customer must be shown $299.00, not $29,900.00.
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ReadsBillingIntervalAndUnit()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        var plan = (await client.ListPlansAsync()).Single(p => p.Handle == "eshop-pro");

        Assert.Equal(1, plan.Interval);
        Assert.Equal(BillingIntervalUnit.Month, plan.IntervalUnit);
        Assert.Equal("month", plan.BillingPeriod);
    }

    [Fact]
    public async Task AddressesTheConfiguredProductFamilyByHandle()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(_handler);

        await client.ListPlansAsync();

        // Handles are the durable identifier; numeric ids are reassigned on every re-seed.
        Assert.Contains($"handle:{BillingClientBuilder.ProductFamilyHandle}", _handler.LastRequest.DecodedUri);
    }

    [Fact]
    public async Task ExcludesArchivedPlans()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductListWithArchived());
        var client = BillingClientBuilder.Build(_handler);

        var plans = await client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task ReturnsEmptyWhenTheFamilyHasNoPlans()
    {
        _handler.RespondOk(HttpMethod.Get, "/products.json", "[]");
        var client = BillingClientBuilder.Build(_handler);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ThrowsTypedExceptionWhenTheFamilyHandleDoesNotResolve()
    {
        // This operation reports a 404 as a bare JSON string rather than an error object.
        _handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.NotFound, "\"Product family not found\"");
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(404, exception.StatusCode);
        Assert.True(exception.IsNotFound);
        Assert.Contains("Product family not found", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAnUninterpretableErrorBodyAsATypedException()
    {
        // The provider answered with an error whose shape the SDK cannot deserialise. That must
        // still leave the seam as a BillingProviderException — never as a raw JsonException.
        _handler.Respond(HttpMethod.Get, "/products.json", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.True(exception.IsTransport);
        Assert.Contains("could not be interpreted", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderAsATypedException()
    {
        _handler.Unreachable(HttpMethod.Get, "/products.json");
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.True(exception.IsTransport);
        Assert.Equal(BillingProviderException.NoStatusCode, exception.StatusCode);
    }
}
