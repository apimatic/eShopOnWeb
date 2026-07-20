using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC0 seed verification + product/component magnitude correctness, against the real sandbox.
/// Asserts against stable facts (handles, prices, kind) rather than numeric ids: Maxio
/// reassigns numeric ids whenever the catalog is re-seeded, so a hard-coded expected id would
/// make this test flaky across sandbox resets (plan.md §1.3) — the resolution-by-handle
/// behaviour itself is what these tests exist to prove.
/// </summary>
[Collection(MaxioCollection.Name)]
public class ProductCatalogTests
{
    private readonly MaxioFixture _fixture;

    public ProductCatalogTests(MaxioFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProductFamilyAsync_ResolvesConfiguredHandleToALiveId()
    {
        var family = await _fixture.BillingClient.GetProductFamilyAsync();

        Assert.True(family.Id > 0);
        Assert.Equal(_fixture.Settings.ProductFamilyHandle, family.Handle);
    }

    [Fact]
    public async Task GetPlanByHandleAsync_EshopPro_ResolvesWithCorrectMagnitude()
    {
        var plan = await _fixture.BillingClient.GetPlanByHandleAsync("eshop-pro");

        Assert.True(plan.Id > 0);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task GetPlanByHandleAsync_BasicPlan_ResolvesWithCorrectMagnitude()
    {
        var plan = await _fixture.BillingClient.GetPlanByHandleAsync("basic-plan");

        Assert.True(plan.Id > 0);
        Assert.Equal("basic-plan", plan.Handle);
        Assert.Equal(2900, plan.PriceInCents);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task GetPlanByHandleAsync_UnknownHandle_ThrowsBillingConfigurationException()
    {
        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.BillingClient.GetPlanByHandleAsync("no-such-plan-handle-xyz"));
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsBothConfiguredPlans_WithIdsConsistentWithHandleLookup()
    {
        var plans = await _fixture.BillingClient.ListPlansAsync();
        var pro = await _fixture.BillingClient.GetPlanByHandleAsync("eshop-pro");
        var basic = await _fixture.BillingClient.GetPlanByHandleAsync("basic-plan");

        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Id == pro.Id && p.PriceInCents == 29900);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Id == basic.Id && p.PriceInCents == 2900);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ReturnsApiCallComponentAsMetered()
    {
        var component = await _fixture.BillingClient.GetMeteredComponentAsync();

        Assert.True(component.Id > 0);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal(1, component.PricePerUnitInCents);
        Assert.Equal(0.01m, component.PricePerUnit);
    }
}
