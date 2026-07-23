using System.Net;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

public class PlanReadTests
{
    [Fact]
    public async Task ListPlansConvertsIntegerCentsIntoWholeCurrencyUnits()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.Build(handler);

        var plans = await client.ListPlansAsync();

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        var basic = Assert.Single(plans, plan => plan.Handle == "basic-plan");

        // 29900 cents is $299.00 — not $29,900 and not $2.99.
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansMapsTheBillingIntervalAndIdentity()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.Build(handler);

        var plans = await client.ListPlansAsync();

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        Assert.Equal(7130997, pro.Id);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal("Everything included", pro.Description);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListPlansTargetsTheConfiguredFamilyByItsDurableHandle()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.Build(handler);

        await client.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", request.Path);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedPlans()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamilyWithArchived);
        var client = BillingClientBuilder.Build(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ListPlansReturnsEmptyWhenTheFamilyHasNoPlans()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.EmptyList);
        var client = BillingClientBuilder.Build(handler);

        Assert.Empty(await client.ListPlansAsync());
    }

    [Fact]
    public async Task FindPlanByHandleReturnsTheResolvedPlan()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProProduct);
        var client = BillingClientBuilder.Build(handler);

        var plan = await client.FindPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("/products/handle/eshop-pro.json", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task FindPlanByHandleReturnsNullForAnUnknownHandle()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindPlanByHandleAsync("no-such-plan"));
    }

    [Fact]
    public async Task FindPlanByHandleTreatsAnArchivedPlanAsUnavailable()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ArchivedProduct);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindPlanByHandleAsync("retired-plan"));
    }

    [Fact]
    public async Task FindPlanByHandleRefusesAPlanFromAnotherProductFamily()
    {
        // Guards against enrolling a customer in a same-named plan that belongs to someone else.
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ForeignFamilyProduct);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindPlanByHandleAsync("eshop-pro"));
    }

    [Fact]
    public async Task FindPlanByHandleMakesNoCallForAnEmptyHandle()
    {
        var handler = new StubHttpMessageHandler();
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindPlanByHandleAsync("  "));
        Assert.Equal(0, handler.RequestCount);
    }
}
