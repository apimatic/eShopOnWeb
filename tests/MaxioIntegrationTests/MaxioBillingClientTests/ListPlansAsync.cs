using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlansAsync
{
    [Fact]
    public async Task ReadsEveryPlanOutOfMaxiosArrayOfWrapperObjects()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList(
            MaxioJson.Product(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 29900),
            MaxioJson.Product(MaxioJson.BasicPlanId, "basic-plan", "Basic Plan", 2900)));

        var plans = await builder.Build().ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, plans.Select(p => p.Handle));
    }

    [Fact]
    public async Task ConvertsIntegerCentsIntoTheCorrectDollarAmount()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList(
            MaxioJson.Product(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 29900),
            MaxioJson.Product(MaxioJson.BasicPlanId, "basic-plan", "Basic Plan", 2900)));

        var plans = await builder.Build().ListPlansAsync();

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(2900, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ReadsTheBillingIntervalAndPaymentMethodRequirement()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList(
            MaxioJson.Product(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 29900,
                requireCreditCard: true)));

        var plan = Assert.Single(await builder.Build().ListPlansAsync());

        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.True(plan.RequiresPaymentMethod);
        Assert.Equal(7130993, plan.ProviderProductId);
        Assert.Equal("Pro Plan", plan.Name);
    }

    [Fact]
    public async Task ExcludesArchivedPlansSoCustomersCannotSubscribeToThem()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList(
            MaxioJson.Product(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 29900),
            MaxioJson.Product("999", "retired-plan", "Retired Plan", 100,
                archivedAt: "2026-01-01T00:00:00-05:00")));

        var plans = await builder.Build().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionWhenTheFamilyHasNoPlans()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", "[]");

        var plans = await builder.Build().ListPlansAsync();

        Assert.Empty(plans);
    }
}
