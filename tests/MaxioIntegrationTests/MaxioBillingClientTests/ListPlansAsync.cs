using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlansAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReturnsEveryPlanInTheConfiguredFamily()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductList(
            ProviderPayloads.ProPlanProduct, ProviderPayloads.BasicPlanProduct));

        var plans = await BillingClientFixture.Create(_handler).ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Collection(plans,
            pro =>
            {
                Assert.Equal("eshop-pro", pro.Handle);
                Assert.Equal("Pro Plan", pro.Name);
                Assert.Equal(7130999, pro.Id);
            },
            basic => Assert.Equal("basic-plan", basic.Handle));
    }

    [Fact]
    public async Task ConvertsThePriceFromCentsIntoWholeCurrencyUnits()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductList(
            ProviderPayloads.ProPlanProduct, ProviderPayloads.BasicPlanProduct));

        var plans = await BillingClientFixture.Create(_handler).ListPlansAsync();

        // 29900 cents is $299.00 — not $29,900 and not $2.99.
        Assert.Equal(299.00m, plans.First().Price);
        Assert.Equal(29.00m, plans.Last().Price);
    }

    [Fact]
    public async Task CarriesTheBillingCadenceThrough()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductList(ProviderPayloads.ProPlanProduct));

        var plan = (await BillingClientFixture.Create(_handler).ListPlansAsync()).Single();

        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionWhenTheFamilyHasNoPlans()
    {
        _handler.RespondWithJson("[]");

        var plans = await BillingClientFixture.Create(_handler).ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task AddressesTheFamilyByItsStableHandleAndTargetsTheConfiguredBaseUrl()
    {
        _handler.RespondWithJson("[]");

        await BillingClientFixture.Create(_handler).ListPlansAsync();

        var request = _handler.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith(BillingClientFixture.StubBaseUrl, request.Uri.ToString());
        Assert.Contains("handle%3Aeshop-subscribe", request.Uri.ToString());
        Assert.Contains("per_page=200", request.Uri.Query);
    }

    [Fact]
    public async Task AuthenticatesWithTheApiKeyAsTheBasicAuthUsername()
    {
        _handler.RespondWithJson("[]");

        await BillingClientFixture.Create(_handler).ListPlansAsync();

        var request = _handler.LastRequest;
        Assert.Equal("Basic", request.AuthorizationScheme);

        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.AuthorizationParameter!));
        Assert.Equal($"{BillingClientFixture.ApiKey}:x", decoded);
    }

    [Fact]
    public async Task ReportsAnUnresolvableFamilyAsAConfigurationProblemRatherThanAProviderFailure()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound, "\"Product family not found\"");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(_handler).ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderAsATypedBillingFailure()
    {
        _handler.AlwaysFailTransport();

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).ListPlansAsync());

        Assert.Equal(0, exception.StatusCode);
    }
}
