using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListPlans
{
    private readonly RecordingHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReturnsThePlansInTheConfiguredFamily()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var plans = await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro");
        Assert.Contains(plans, p => p.Handle == "basic-plan");
    }

    /// <summary>
    /// Maxio reports money in cents. Getting the magnitude wrong would show a $299 plan as $29,900
    /// or $2.99, so both representations are pinned.
    /// </summary>
    [Fact]
    public async Task ReportsPriceInCentsAndInDollarsWithTheCorrectMagnitude()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var plans = await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(2900, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task CarriesTheBillingIntervalAndPaymentMethodRequirement()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var pro = (await TestBillingClientFactory.Create(_handler).ListPlansAsync())
            .Single(p => p.Handle == "eshop-pro");

        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
    }

    [Fact]
    public async Task ExcludesArchivedPlansSoRetiredPlansAreNeverOffered()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.ProductsWithArchived);

        var plans = await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionForAFamilyWithNoPlans()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.EmptyArray);

        var plans = await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        Assert.Empty(plans);
    }

    /// <summary>
    /// Numeric IDs are reassigned whenever the catalog is re-created, so the family must be
    /// resolved from its configured handle rather than a stored ID.
    /// </summary>
    [Fact]
    public async Task ResolvesTheFamilyIdFromItsHandleBeforeReadingPlans()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        Assert.Collection(_handler.Requests,
            first => Assert.Equal(MaxioResponses.FamilyPath, first.Path),
            second => Assert.Equal($"/product_families/{MaxioResponses.FamilyId}/products.json", second.Path));
    }

    [Fact]
    public async Task FailsWithAConfigurationErrorWhenTheConfiguredFamilyDoesNotExist()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.EmptyArray);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());
        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task FailsWithAConfigurationErrorWhenNoFamilyHandleIsConfigured()
    {
        var settings = TestBillingClientFactory.Settings(s => s.ProductFamilyHandle = string.Empty);
        var client = TestBillingClientFactory.Create(_handler, settings);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task FindsAPlanByItsHandleIgnoringCase()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var plan = await TestBillingClientFactory.Create(_handler).FindPlanByHandleAsync("ESHOP-PRO");

        Assert.NotNull(plan);
        Assert.Equal(MaxioResponses.ProPlanId, plan.Id);
    }

    [Fact]
    public async Task ReturnsNullForAPlanHandleThatDoesNotResolve()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var plan = await TestBillingClientFactory.Create(_handler).FindPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Fact]
    public async Task AuthenticatesWithHttpBasicUsingTheApiKeyAsTheUsername()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        await TestBillingClientFactory.Create(_handler).ListPlansAsync();

        var authorization = _handler.Requests[0].Authorization;
        Assert.NotNull(authorization);
        Assert.StartsWith("Basic ", authorization);

        var decoded = System.Text.Encoding.ASCII.GetString(
            Convert.FromBase64String(authorization["Basic ".Length..]));
        Assert.Equal("test-api-key:x", decoded);
    }
}
