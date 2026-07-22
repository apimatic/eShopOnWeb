using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Read paths: the client must reach the right Maxio route, convert money out of integer cents,
/// and turn "no such thing" into an empty result or null rather than an exception.
/// </summary>
public class MaxioBillingClientReadTests
{
    [Fact]
    public async Task ListPlansReadsTheConfiguredFamilyByItsDurableHandle()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "product_families/handle:eshop-subscribe/products.json", MaxioPayloads.ProductList);

        var plans = await BillingClientFactory.Create(server).ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Single(server.RequestsFor(HttpMethod.Get, "product_families"));
    }

    [Fact]
    public async Task PlanPricesArriveInMajorUnitsNotCents()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "product_families", MaxioPayloads.ProductList);

        var plans = await BillingClientFactory.Create(server).ListPlansAsync();

        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        var basic = plans.Single(plan => plan.Handle == "basic-plan");

        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(29.00m, basic.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ArchivedPlansAreNotOfferedToCustomers()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "product_families", MaxioPayloads.ProductList);

        var plans = await BillingClientFactory.Create(server).ListPlansAsync();

        Assert.DoesNotContain(plans, plan => plan.Handle == "retired-plan");
    }

    [Fact]
    public async Task AnEmptyCatalogListsNoPlansRatherThanFailing()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "product_families", "[]")
            .Respond(HttpMethod.Get, "products.json", "[]");

        var plans = await BillingClientFactory.Create(server).ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task AnUnknownFamilyFallsBackToTheSiteWideCatalogFilteredByFamily()
    {
        // The family-scoped routes 404 (stale id/handle after a re-seed), so the site-wide list is
        // used and narrowed to the configured family.
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "products.json", MaxioPayloads.ProductList);

        var plans = await BillingClientFactory.Create(server, settings => settings.ProductFamilyHandle = "eshop-subscribe")
            .ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle));
    }

    [Fact]
    public async Task GetPlanByHandleReadsTheHandleRoute()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "products/handle/eshop-pro.json", MaxioPayloads.ProPlanProduct);

        var plan = await BillingClientFactory.Create(server).GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal(7126957, plan!.Id);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task AnUnknownPlanHandleReadsAsNull()
    {
        var server = new FakeMaxioServer();

        var plan = await BillingClientFactory.Create(server).GetPlanByHandleAsync("does-not-exist");

        Assert.Null(plan);
    }

    [Fact]
    public async Task AnEmptyPlanHandleNeverReachesTheProvider()
    {
        var server = new FakeMaxioServer();

        var plan = await BillingClientFactory.Create(server).GetPlanByHandleAsync("  ");

        Assert.Null(plan);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task AMeteredComponentReadsItsKindAndItsStringUnitPrice()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "components/lookup.json?handle=api-call", MaxioPayloads.MeteredComponent);

        var component = await BillingClientFactory.Create(server).GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component!.IsMetered);
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal(3057195, component.Id);
        Assert.Equal("per_unit", component.PricingScheme);
    }

    [Fact]
    public async Task AQuantityBasedComponentIsNotAcceptedAsMetered()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "components/lookup.json", MaxioPayloads.QuantityBasedComponent);

        var component = await BillingClientFactory.Create(server).GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
    }

    [Fact]
    public async Task ATieredComponentHasNoSingleUnitPrice()
    {
        // Maxio only populates unit_price for per-unit pricing schemes; other schemes send null.
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "components/lookup.json", MaxioPayloads.TieredMeteredComponent);

        var component = await BillingClientFactory.Create(server).GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component!.IsMetered);
        Assert.Null(component.UnitPrice);
    }

    [Fact]
    public async Task AnUnknownComponentHandleReadsAsNull()
    {
        var component = await BillingClientFactory.Create(new FakeMaxioServer()).GetComponentByHandleAsync("nope");

        Assert.Null(component);
    }

    [Fact]
    public async Task ACustomerIsLookedUpByTheEShopUserReference()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "customers/lookup.json", MaxioPayloads.Customer);

        var customer = await BillingClientFactory.Create(server).FindCustomerByReferenceAsync("demo@microsoft.com");

        Assert.NotNull(customer);
        Assert.Equal(14714298, customer!.Id);
        Assert.Equal("demo@microsoft.com", customer.Reference);
        Assert.Contains("reference=demo%40microsoft.com", server.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task AnUnknownCustomerReferenceReadsAsNull()
    {
        var customer = await BillingClientFactory.Create(new FakeMaxioServer()).FindCustomerByReferenceAsync("nobody@example.com");

        Assert.Null(customer);
    }

    [Fact]
    public async Task ACustomerWithNoSubscriptionsListsEmpty()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "customers/14714298/subscriptions.json", "[]");

        var subscriptions = await BillingClientFactory.Create(server).ListCustomerSubscriptionsAsync(14714298);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task AnUnknownCustomerListsEmptyRatherThanThrowing()
    {
        var subscriptions = await BillingClientFactory.Create(new FakeMaxioServer()).ListCustomerSubscriptionsAsync(999);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ASubscriptionIsNormalizedWithMoneyInMajorUnits()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", MaxioPayloads.Subscription());

        var subscription = await BillingClientFactory.Create(server).GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStates.Active, subscription!.State);
        Assert.Equal(299.00m, subscription.ProductPrice);
        Assert.Equal(12.50m, subscription.Balance);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(14714298, subscription.CustomerId);
        Assert.Equal("demo@microsoft.com", subscription.CustomerReference);
        Assert.False(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2024, 2, 15, 14, 48, 10, TimeSpan.FromHours(-5)), subscription.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task AnUnknownSubscriptionIdReadsAsNull()
    {
        var subscription = await BillingClientFactory.Create(new FakeMaxioServer()).GetSubscriptionAsync(404404);

        Assert.Null(subscription);
    }

    [Fact]
    public async Task TheUsageTotalComesFromTheComponentLineItemBalance()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915/components/handle:api-call.json", MaxioPayloads.SubscriptionComponentWithBalance);

        var total = await BillingClientFactory.Create(server).GetUsageTotalAsync(15236915, "api-call");

        Assert.Equal(175m, total);
    }

    [Fact]
    public async Task WithNoLineItemBalanceTheUsageTotalIsSummedFromTheRecordedUsages()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "components/handle:api-call.json", MaxioPayloads.SubscriptionComponentWithoutBalance)
            .Respond(HttpMethod.Get, "components/handle:api-call/usages.json", MaxioPayloads.UsageList);

        var total = await BillingClientFactory.Create(server).GetUsageTotalAsync(15236915, "api-call");

        // Maxio reports one quantity as the string "20.0" and the other as the number 5.
        Assert.Equal(25m, total);
    }

    [Fact]
    public async Task AnUnknownSubscriptionHasNoUsageTotalRatherThanZero()
    {
        var total = await BillingClientFactory.Create(new FakeMaxioServer()).GetUsageTotalAsync(404404, "api-call");

        Assert.Null(total);
    }
}
