using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Reading the plan catalogue, including the cents-to-dollars conversion that decides what a
/// customer is shown.
/// </summary>
public class MaxioBillingClientPlanTests
{
    [Fact]
    public async Task ListPlansAsync_ConvertsCentsToDollars()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList(
            MaxioJson.Product(handle: "eshop-pro", name: "Pro Plan", priceInCents: MaxioJson.ProPlanCents),
            MaxioJson.Product(id: 7130998, handle: "basic-plan", name: "Basic Plan", priceInCents: MaxioJson.BasicPlanCents)));

        var plans = await BillingClientFixture.Create(handler).ListPlansAsync();

        Assert.Equal(2, plans.Count);

        // 29900 cents is $299.00 — not $29900 and not $2.99.
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(299.00m, pro.Price);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansAsync_MapsTheFullPlanShape()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList(MaxioJson.Product()));

        var plan = Assert.Single(await BillingClientFixture.Create(handler).ListPlansAsync());

        Assert.Equal(7130997, plan.Id);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal(BillingClientFixture.FamilyHandle, plan.ProductFamilyHandle);
        Assert.False(plan.RequiresPaymentMethod);
        Assert.False(plan.IsArchived);
    }

    [Fact]
    public async Task ListPlansAsync_RequestsTheConfiguredProductFamilyByHandle()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList());

        await BillingClientFixture.Create(handler).ListPlansAsync();

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);

        // The family must be addressed by its durable handle, not a numeric id that moves on re-seed.
        Assert.Contains($"handle:{BillingClientFixture.FamilyHandle}", Uri.UnescapeDataString(request.Path));
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedPlans()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList(
            MaxioJson.Product(handle: "eshop-pro"),
            MaxioJson.Product(id: 7130999, handle: "retired-plan", archived: true)));

        var plans = await BillingClientFixture.Create(handler).ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsEmpty_WhenTheFamilyHoldsNoPlans()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("[]");

        Assert.Empty(await BillingClientFixture.Create(handler).ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansAsync_OrdersPlansByPriceAscending()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList(
            MaxioJson.Product(handle: "eshop-pro", priceInCents: MaxioJson.ProPlanCents),
            MaxioJson.Product(id: 7130998, handle: "basic-plan", priceInCents: MaxioJson.BasicPlanCents)));

        var plans = await BillingClientFixture.Create(handler).ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle).ToArray());
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAMissingFamily_AsAConfigurationError()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("\"Product Family not found\"", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(handler).ListPlansAsync());

        Assert.Contains(BillingClientFixture.FamilyHandle, ex.Message);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAProviderOutage_AsATypedProviderError()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("<html>502 Bad Gateway</html>", HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ListPlansAsync());

        Assert.Equal(502, ex.StatusCode);
        Assert.False(ex.IsNotFound);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesBadCredentials_AsA401()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Unauthorized"), HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ListPlansAsync());

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsThePlan()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.ProductEnvelope(MaxioJson.Product(handle: "eshop-pro", priceInCents: MaxioJson.ProPlanCents)));

        var plan = await BillingClientFixture.Create(handler).FindPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsNull_ForAnUnknownHandle()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.Errors("Not Found"), HttpStatusCode.NotFound);

        Assert.Null(await BillingClientFixture.Create(handler).FindPlanByHandleAsync("no-such-plan"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task FindPlanByHandleAsync_ReturnsNull_WithoutCallingTheProvider_ForABlankHandle(string? blank)
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("{}");

        Assert.Null(await BillingClientFixture.Create(handler).FindPlanByHandleAsync(blank!));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_Throws_OnANonNotFoundFailure()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("boom"), HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).FindPlanByHandleAsync("eshop-pro"));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task Client_AuthenticatesWithTheApiKeyAsUsername()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList());

        await BillingClientFixture.Create(handler).ListPlansAsync();

        var parameter = handler.LastRequest.AuthorizationParameter;
        Assert.NotNull(parameter);

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
        Assert.Equal("test-api-key:x", decoded);
    }
}
