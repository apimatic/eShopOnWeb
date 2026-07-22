using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class MaxioBillingClientReadTests
{
    [Fact]
    public async Task ListPlansReadsTheConfiguredFamilyByHandle()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlanProduct, MaxioJson.BasicPlanProduct));

        var plans = await builder.Build().ListPlansAsync();

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", request.Path);
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public async Task PlanPricesAreConvertedFromMinorUnitsToTheSiteCurrency()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlanProduct, MaxioJson.BasicPlanProduct));

        var plans = await builder.Build().ListPlansAsync();

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        var basic = plans.Single(p => p.Handle == "basic-plan");

        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(29.00m, basic.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ArchivedPlansAreNotOffered()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK,
            MaxioJson.ProductList(MaxioJson.ProPlanProduct, MaxioJson.ArchivedProduct));

        var plans = await builder.Build().ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task AFamilyWithNoPlansListsNothing()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, "[]");

        var plans = await builder.Build().ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task AnUnknownPlanHandleReadsAsNull()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound, "\"Not found\"");

        var plan = await builder.Build().GetPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
        Assert.Equal("/products/handle/no-such-plan.json", Assert.Single(builder.Transport.Requests).Path);
    }

    [Fact]
    public async Task AKnownPlanHandleReadsThePlan()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ProPlanProduct);

        var plan = await builder.Build().GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal(7126957, plan!.Id);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task ACustomerIsLookedUpByTheEShopUserReference()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.Customer);

        var customer = await builder.Build().FindCustomerByReferenceAsync("demouser@microsoft.com");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal("/customers/lookup.json", request.Path);
        Assert.Contains("reference=demouser%40microsoft.com", request.Query, StringComparison.Ordinal);
        Assert.Equal(88833369, customer!.Id);
        Assert.Equal("demouser@microsoft.com", customer.Reference);
    }

    [Fact]
    public async Task AUserWithNoCustomerRecordReadsAsNull()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound);

        var customer = await builder.Build().FindCustomerByReferenceAsync("stranger@microsoft.com");

        Assert.Null(customer);
    }

    [Fact]
    public async Task AnUnknownSubscriptionIdReadsAsNull()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound);

        var subscription = await builder.Build().GetSubscriptionAsync(404404);

        Assert.Null(subscription);
        Assert.Equal("/subscriptions/404404.json", Assert.Single(builder.Transport.Requests).Path);
    }

    [Fact]
    public async Task ASubscriptionCarriesItsPlanPriceStateAndNextBillingDate()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        var subscription = await builder.Build().GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.Equal("active", subscription!.State);
        Assert.Equal(299.00m, subscription.ProductPrice);
        Assert.Equal(0m, subscription.Balance);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(88833369, subscription.CustomerId);
        Assert.Equal("demouser@microsoft.com", subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-5)), subscription.NextBillingAt);
        Assert.False(subscription.CancelAtEndOfPeriod);
    }

    [Fact]
    public async Task ACustomerWithNoSubscriptionsListsNothing()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, "[]");

        var subscriptions = await builder.Build().ListCustomerSubscriptionsAsync(88833369);

        Assert.Empty(subscriptions);
        Assert.Equal("/customers/88833369/subscriptions.json", Assert.Single(builder.Transport.Requests).Path);
    }

    [Fact]
    public async Task ACustomersSubscriptionsAreListed()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.SubscriptionList(MaxioJson.ActiveSubscription));

        var subscriptions = await builder.Build().ListCustomerSubscriptionsAsync(88833369);

        Assert.Equal(15236915, Assert.Single(subscriptions).Id);
    }

    [Fact]
    public async Task AMeteredComponentIsRecognizedAndItsUnitPriceParsed()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.MeteredComponent);

        var component = await builder.Build().GetComponentByHandleAsync("api-call");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal("/components/lookup.json", request.Path);
        Assert.Contains("handle=api-call", request.Query, StringComparison.Ordinal);
        Assert.True(component!.IsMetered);
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal(3023074, component.ProductFamilyId);
    }

    [Fact]
    public async Task AComponentOfTheWrongKindIsNotReportedAsMetered()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.QuantityBasedComponent);

        var component = await builder.Build().GetComponentByHandleAsync("api-call");

        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task AnUnknownComponentHandleReadsAsNull()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound);

        Assert.Null(await builder.Build().GetComponentByHandleAsync("no-such-component"));
    }

    [Fact]
    public async Task ThePeriodToDateTotalIsReadFromTheSubscriptionComponent()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, MaxioJson.SubscriptionComponent);

        var total = await builder.Build().GetUsageTotalAsync(15236915, "api-call");

        var request = Assert.Single(builder.Transport.Requests);
        Assert.Equal("/subscriptions/15236915/components/handle:api-call.json", request.Path);
        Assert.Equal(250m, total!.UnitBalance);
        Assert.Equal(3057195, total.ComponentId);
    }

    [Fact]
    public async Task AComponentNotAttachedToTheSubscriptionHasNoTotal()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound);

        Assert.Null(await builder.Build().GetUsageTotalAsync(15236915, "api-call"));
    }
}
