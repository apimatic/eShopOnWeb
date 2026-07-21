using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class ListPlansAsyncTests
{
    [Fact]
    public async Task ReturnsPlansWithCorrectlyConvertedMoneyAndInterval()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            [
              { "product": { "id": 7127070, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
              { "product": { "id": 7127071, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
            ]
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.BillingIntervalUnit);
        Assert.Equal(1, pro.BillingIntervalCount);
        Assert.False(pro.RequiresPaymentMethod);

        var basic = Assert.Single(plans, p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ReturnsEmptyListWhenProductFamilyHasNoProducts()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Json(HttpStatusCode.OK, "[]"));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task PagesThroughMultipleResultPagesUntilAShortPageIsReturned()
    {
        // First page is exactly UsageListPerPage (50) items long, forcing a second page fetch.
        var firstPage = "[" + string.Join(",", Enumerable.Range(1, 50)
            .Select(i => $$"""{ "product": { "id": {{i}}, "name": "Plan {{i}}", "handle": "plan-{{i}}", "price_in_cents": 1000, "interval": 1, "interval_unit": "month" } }"""))
            + "]";
        var secondPage = """[{ "product": { "id": 999, "name": "Last Plan", "handle": "plan-last", "price_in_cents": 500, "interval": 1, "interval_unit": "month" } }]""";

        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, firstPage),
            SequentialStubHandler.Json(HttpStatusCode.OK, secondPage));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(51, plans.Count);
        Assert.Contains(plans, p => p.Handle == "plan-last");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenProductFamilyDoesNotResolve()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.NotFound, "\"Product Family not found\""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("not found", ex.Message);
    }
}
