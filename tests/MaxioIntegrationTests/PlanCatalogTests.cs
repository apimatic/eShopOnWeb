using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC1 step 1 — listing and resolving the plans a customer can subscribe to.</summary>
public class PlanCatalogTests
{
    [Fact]
    public async Task ListPlansConvertsMinorUnitsToWholeCurrencyUnits()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var plans = await context.Client.ListPlansAsync();

        Assert.Equal(2, plans.Count);

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        // 29900 minor units is $299.00 — not $29,900 and not $2.99.
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(7130997, pro.Id);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansAddressesTheFamilyByHandleNotByReassignableId()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        await context.Client.ListPlansAsync();

        // Numeric ids are reassigned on a re-seed; the handle is the durable identifier (§1.3).
        Assert.Equal(1, context.Server.CountRequests(HttpMethod.Get, MaxioTestContext.PlansRoute));
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanListWithArchived));

        var plans = await context.Client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task ListPlansReturnsEmptyWhenTheFamilyHasNoProducts()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.EmptyList));

        var plans = await context.Client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task GetPlanByHandleResolvesAKnownHandle()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var plan = await context.Client.GetPlanByHandleAsync("basic-plan");

        Assert.NotNull(plan);
        Assert.Equal("basic-plan", plan!.Handle);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task GetPlanByHandleReturnsNullForAnUnknownHandle()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        // A stale handle after a re-seed must not silently resolve to some other plan.
        Assert.Null(await context.Client.GetPlanByHandleAsync("no-such-plan"));
    }

    [Fact]
    public async Task GetPlanByHandleReturnsNullForABlankHandleWithoutCallingTheProvider()
    {
        var context = new MaxioTestContext();

        Assert.Null(await context.Client.GetPlanByHandleAsync("  "));
        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public async Task ListPlansSurfacesAProviderRejectionAsATypedException()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute,
            new FakeResponse(System.Net.HttpStatusCode.Unauthorized, """{"errors":["Authentication failed"]}"""));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => context.Client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Contains("Authentication failed", exception.Message);
    }

    [Fact]
    public async Task ListPlansSurfacesAnUnreachableProviderAsATypedException()
    {
        var context = new MaxioTestContext();
        context.Server.TransportFailure = new HttpRequestException("No such host is known.");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => context.Client.ListPlansAsync());

        Assert.Contains("could not be reached", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ListPlansSurfacesAnUnreadableResponseAsATypedException()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok("this is not json"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => context.Client.ListPlansAsync());

        Assert.Contains("could not be read", exception.Message);
    }
}
