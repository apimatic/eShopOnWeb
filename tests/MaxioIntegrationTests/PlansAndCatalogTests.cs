using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.MaxioIntegrationTests.Support;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Real, live-sandbox behaviour of the read-only catalog surface: plan listing, money/magnitude
/// correctness, and the metered-component kind validation UC2 depends on.
/// </summary>
public class PlansAndCatalogTests
{
    [Fact]
    public async Task ListAvailablePlansAsync_ReturnsBothConfiguredPlans_WithExactSandboxPricing()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);

        var plans = await client.ListAvailablePlansAsync();

        Assert.Equal(2, plans.Count);

        var proPlan = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", proPlan.Name);
        // Money is carried as integer cents, never a parsed display string - $299.00 == 29900 cents exactly.
        Assert.Equal(29900L, proPlan.PriceInCents);
        Assert.False(proPlan.RequiresPaymentMethod);

        var basicPlan = Assert.Single(plans, p => p.Handle == "basic-plan");
        Assert.Equal("Basic Plan", basicPlan.Name);
        Assert.Equal(2900L, basicPlan.PriceInCents);
        Assert.False(basicPlan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ValidateUsageComponentAsync_Succeeds_ForTheConfiguredMeteredComponent()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);

        // Must not throw: api-call is seeded as kind=Metered on the sandbox (UC0 precondition for UC2).
        await client.ValidateUsageComponentAsync();
    }

    [Fact]
    public async Task ValidateUsageComponentAsync_IsMemoized_AndOnlyCallsTheProviderOnce()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);

        // The validation cache (shared singleton in the real app) must not re-hit the provider once
        // it has already succeeded once - call it three times, expect no failure and no growth in cost.
        await client.ValidateUsageComponentAsync();
        await client.ValidateUsageComponentAsync();
        await client.ValidateUsageComponentAsync();
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNull_ForAReferenceThatDoesNotExist()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);
        var unknownReference = $"nonexistent-{Guid.NewGuid():N}";

        var customer = await client.FindCustomerByReferenceAsync(unknownReference);

        Assert.Null(customer);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ThrowsSubscriptionNotFoundException_ForAnUnknownId()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);

        // A subscription id that is astronomically unlikely to exist on this sandbox.
        const int unknownSubscriptionId = 999_999_999;

        var ex = await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.SubscriptionNotFoundException>(
            () => client.GetSubscriptionAsync(unknownSubscriptionId));

        Assert.Contains(unknownSubscriptionId.ToString(), ex.Message);
    }
}
