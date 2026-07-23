using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Listing and resolving plans, including money magnitude and archived-plan handling.</summary>
public class CatalogReadTests
{
    [Fact]
    public async Task ListPlansReturnsEveryPlanWithPricesInCentsAndInCurrency()
    {
        var (client, _) = BillingClientFixture.Create(
            ProviderPayloads.PlanList(ProviderPayloads.ProPlan, ProviderPayloads.BasicPlan));

        var plans = (await client.ListPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900L, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.BillingCadence);
        Assert.False(pro.IsArchived);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(2900L, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task PlanPricesAreNotConfusedBetweenCentsAndDollars()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.PlanList(ProviderPayloads.ProPlan));

        var plan = (await client.ListPlansAsync()).Single();

        // 29900 cents is $299.00 — never $29,900.00 and never $2.99.
        Assert.Equal(299.00m, plan.Price);
        Assert.NotEqual(29900m, plan.Price);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedPlans()
    {
        var (client, _) = BillingClientFixture.Create(
            ProviderPayloads.PlanList(ProviderPayloads.ProPlan, ProviderPayloads.ArchivedPlan));

        var plans = await client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans.Single().Handle);
    }

    [Fact]
    public async Task ListPlansReturnsAnEmptyCollectionWhenTheFamilyHasNoPlans()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.EmptyList);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlansScopesTheRequestToTheConfiguredProductFamilyByHandle()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.EmptyList);

        await client.ListPlansAsync();

        var path = Uri.UnescapeDataString(handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains($"handle:{BillingClientFixture.ProductFamilyHandle}", path);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task ListPlansSurfacesAProviderFailureAsATypedException()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal("ListPlans", exception.Operation);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task FindPlanByHandleReturnsTheResolvedPlan()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.ProPlan);

        var plan = await client.FindPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan!.Handle);
        Assert.Equal(29900L, plan.PriceInCents);
    }

    [Fact]
    public async Task FindPlanByHandleReturnsNullForAnUnknownHandle()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        var plan = await client.FindPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Fact]
    public async Task FindPlanByHandleReturnsNullForAnEmptyHandleWithoutCallingTheProvider()
    {
        var (client, handler) = BillingClientFixture.Create();

        var plan = await client.FindPlanByHandleAsync("   ");

        Assert.Null(plan);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ABadApiKeyIsNeverMistakenForAMissingPlan()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");

        // Returning null here would silently present an authentication failure as "no such plan".
        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.FindPlanByHandleAsync("eshop-pro"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task AForbiddenResponseIsNeverMistakenForAMissingPlan()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.FindPlanByHandleAsync("eshop-pro"));
    }

    [Fact]
    public async Task AServerFaultIsNeverMistakenForAMissingPlan()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.FindPlanByHandleAsync("eshop-pro"));
    }
}
