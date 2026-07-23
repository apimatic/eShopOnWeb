using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Reading the plan catalog (UC1, step 1), and how the client addresses and authenticates.</summary>
public class MaxioBillingClientPlanTests
{
    [Fact]
    public async Task ListPlansAsync_ReturnsThePlansOfTheConfiguredFamily_WithPricesConvertedFromMinorUnits()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.PlanListJson);
        var client = TestBillingClient.Create(handler);

        var plans = await client.ListPlansAsync();

        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal(MaxioPayloads.ProPlanId, pro.Id);
        Assert.Equal("Pro Plan", pro.Name);
        // Maxio reports 29900 minor units; the domain must expose $299.00, not $29,900.
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("month", pro.BillingPeriod);

        var basic = Assert.Single(plans, p => p.Handle == "basic-plan");
        Assert.Equal(2900, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansAsync_OmitsArchivedPlans_SoACustomerIsNeverOfferedARetiredPlan()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.PlanListJson);
        var client = TestBillingClient.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsEmpty_WhenTheFamilyHasNoPlans()
    {
        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlansAsync_AddressesTheFamilyByHandle_WhenNoNumericIdIsConfigured()
    {
        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler);

        await client.ListPlansAsync();

        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task ListPlansAsync_AddressesTheFamilyByNumericId_WhenOneIsConfigured()
    {
        var settings = TestBillingClient.Settings();
        settings.ProductFamilyId = 3026729;

        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler, settings);

        await client.ListPlansAsync();

        Assert.Equal("/product_families/3026729/products.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task EveryRequest_AuthenticatesWithBasicAuth_UsingTheApiKeyAndTheLiteralXPassword()
    {
        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler);

        await client.ListPlansAsync();

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{TestBillingClient.ApiKey}:x"));
        Assert.Equal("Basic", handler.LastRequest.AuthScheme);
        Assert.Equal(expected, handler.LastRequest.AuthParameter);
    }

    [Fact]
    public async Task TheClient_TargetsTheHostResolvedFromConfiguration_SoTheSameBuildCanBePointedAtAMock()
    {
        var settings = TestBillingClient.Settings();
        settings.BaseUrl = "http://localhost:8080";

        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler, settings);

        await client.ListPlansAsync();

        Assert.Equal("http://localhost:8080/product_families/handle:eshop-subscribe/products.json",
            handler.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task TheClient_HonoursABaseAddressAlreadySetByTheCompositionRoot()
    {
        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler, baseAddress: new Uri("https://preset.example.com/"));

        await client.ListPlansAsync();

        Assert.Equal("preset.example.com", handler.LastRequest.RequestUri.Host);
    }

    [Fact]
    public void Constructing_Throws_WhenNoApiKeyIsConfigured()
    {
        var settings = TestBillingClient.Settings();
        settings.ApiKey = null;

        var exception = Assert.Throws<BillingConfigurationException>(
            () => TestBillingClient.Create(StubHttpMessageHandler.ReturningOk("[]"), settings));

        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public async Task GetPlanByHandleAsync_ReadsThePlanByItsDurableHandle()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.ProPlanJson);
        var client = TestBillingClient.Create(handler);

        var plan = await client.GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan!.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("/products/handle/eshop-pro.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task GetPlanByHandleAsync_ReturnsNull_WhenTheHandleDoesNotResolve()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson);
        var client = TestBillingClient.Create(handler);

        Assert.Null(await client.GetPlanByHandleAsync("no-such-plan"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPlanByHandleAsync_ReturnsNullWithoutCallingTheProvider_WhenTheHandleIsBlank(string handle)
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.ProPlanJson);
        var client = TestBillingClient.Create(handler);

        Assert.Null(await client.GetPlanByHandleAsync(handle));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAProviderRejectionAsATypedException_CarryingStatusAndMessages()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Unauthorized,
            """{"errors":["Authentication failed"]}""");
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal(new[] { "Authentication failed" }, exception.ProviderErrors);
        Assert.Contains("Authentication failed", exception.Message);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAnUnreachableProviderAsATypedException()
    {
        var client = TestBillingClient.Create(StubHttpMessageHandler.Unreachable());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Null(exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAnUnreadableResponseAsATypedException()
    {
        var handler = StubHttpMessageHandler.ReturningOk("this is not json");
        var client = TestBillingClient.Create(handler);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());
    }

    [Fact]
    public async Task AProviderErrorWithoutAnErrorsArray_StillSurfacesTheStatusAndTheRawBody()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.InternalServerError, "<html>gateway down</html>");
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(500, exception.StatusCode);
        Assert.Empty(exception.ProviderErrors);
        Assert.Contains("gateway down", exception.Message);
    }
}
