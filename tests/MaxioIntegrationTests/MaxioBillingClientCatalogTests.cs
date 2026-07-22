using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Reading the plan and component catalogue out of Maxio (UC1 step 1, UC2 preconditions).</summary>
public class MaxioBillingClientCatalogTests
{
    private const string PlansPath = "product_families/handle:eshop-subscribe/products.json";

    [Fact]
    public async Task ListPlansReadsThePriceInCentsWithoutRescalingIt()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, MaxioPayloads.PlanListJson);
        var client = BillingClientFixture.Create(stub);

        var plans = await client.ListPlansAsync();

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900L, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(2900L, basic.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansMapsTheBillingIntervalAndPaymentRequirement()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, MaxioPayloads.PlanListJson);
        var client = BillingClientFixture.Create(stub);

        var pro = (await client.ListPlansAsync()).Single(p => p.Handle == "eshop-pro");

        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
    }

    [Fact]
    public async Task ListPlansHidesArchivedPlansSoCustomersCannotSubscribeToThem()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, MaxioPayloads.PlanListJson);
        var client = BillingClientFixture.Create(stub);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.DoesNotContain(plans, p => p.Handle == "legacy-plan");
    }

    [Fact]
    public async Task ListPlansReturnsAnEmptyCollectionWhenTheFamilyHasNoProducts()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, "[]");
        var client = BillingClientFixture.Create(stub);

        Assert.Empty(await client.ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansAddressesTheFamilyByItsDurableHandleRatherThanItsNumericId()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, "[]");
        var client = BillingClientFixture.Create(stub);

        await client.ListPlansAsync();

        // Numeric ids are reassigned on a sandbox re-seed; handles are not (plan.md §1.3).
        Assert.Equal(1, stub.CallCount(HttpMethod.Get, PlansPath));
        Assert.DoesNotContain("3026728", stub.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task GetPlanByHandleResolvesAKnownPlan()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "products/handle/eshop-pro.json", MaxioPayloads.ProPlanJson);
        var client = BillingClientFixture.Create(stub);

        var plan = await client.GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal(7130993, plan!.Id);
        Assert.Equal(299.00m, plan.Price);
        Assert.False(plan.IsArchived);
    }

    [Fact]
    public async Task GetPlanByHandleReturnsNullForAnUnknownHandleRatherThanThrowing()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "products/handle/no-such-plan.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var client = BillingClientFixture.Create(stub);

        Assert.Null(await client.GetPlanByHandleAsync("no-such-plan"));
    }

    [Fact]
    public async Task GetPlanByHandleFlagsAnArchivedPlanInsteadOfHidingIt()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "products/handle/legacy-plan.json", MaxioPayloads.RetiredPlanJson);
        var client = BillingClientFixture.Create(stub);

        var plan = await client.GetPlanByHandleAsync("legacy-plan");

        Assert.NotNull(plan);
        Assert.True(plan!.IsArchived);
    }

    [Fact]
    public async Task GetComponentByHandleReadsTheUnitPriceEvenThoughMaxioSendsItAsAString()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "components/lookup.json?handle=api-call",
            MaxioPayloads.MeteredComponentJson);
        var client = BillingClientFixture.Create(stub);

        var component = await client.GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(0.01m, component!.UnitPrice);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.Equal("call", component.UnitName);
    }

    [Fact]
    public async Task GetComponentByHandleRecognisesAMeteredComponent()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "components/lookup.json?handle=api-call",
            MaxioPayloads.MeteredComponentJson);
        var client = BillingClientFixture.Create(stub);

        var component = await client.GetComponentByHandleAsync("api-call");

        Assert.Equal(BillingComponentKind.Metered, component!.Kind);
        Assert.True(component.IsMetered);
    }

    [Fact]
    public async Task GetComponentByHandleDistinguishesANonMeteredComponentOfTheSameHandle()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "components/lookup.json?handle=api-call",
            MaxioPayloads.QuantityBasedComponentJson);
        var client = BillingClientFixture.Create(stub);

        var component = await client.GetComponentByHandleAsync("api-call");

        Assert.Equal(BillingComponentKind.QuantityBased, component!.Kind);
        Assert.False(component.IsMetered);
    }

    [Fact]
    public async Task GetComponentByHandleReturnsNullWhenTheComponentDoesNotExist()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "components/lookup.json?handle=ghost",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var client = BillingClientFixture.Create(stub);

        Assert.Null(await client.GetComponentByHandleAsync("ghost"));
    }

    [Fact]
    public async Task EveryRequestCarriesHttpBasicCredentialsWithTheApiKeyAsUsernameAndXAsPassword()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, "[]");
        var client = BillingClientFixture.Create(stub);

        await client.ListPlansAsync();

        var request = stub.Requests.Single();
        Assert.Equal("Basic", request.AuthScheme);
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.AuthParameter!));
        Assert.Equal($"{BillingClientFixture.ApiKey}:x", decoded);
    }

    [Fact]
    public async Task RequestsGoToTheConfiguredOverrideHostWhenOneIsSet()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.BaseUrl = "http://localhost:8080";

        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, "[]");
        var client = BillingClientFixture.Create(stub, settings);

        await client.ListPlansAsync();

        Assert.Equal(1, stub.CallCount(HttpMethod.Get, PlansPath));
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsATypedBillingFailure()
    {
        var stub = new MaxioApiStub().Unreachable(HttpMethod.Get, PlansPath);
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("Could not reach the billing provider", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task AMalformedResponseBodySurfacesAsATypedBillingFailure()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, PlansPath, "this is not json");
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("Could not read the billing provider's response", exception.Message);
    }
}
