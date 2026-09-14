using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class ListPlansAsync
{
    [Fact]
    public async Task MapsProductsToPlans()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """
                [
                    {
                        "product": {
                            "id": 7126957,
                            "name": "Pro Plan",
                            "handle": "eshop-pro",
                            "price_in_cents": 29900,
                            "interval": 1,
                            "interval_unit": "month",
                            "require_credit_card": false,
                            "taxable": false
                        }
                    }
                ]
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.IntervalCount);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
        Assert.False(plan.HasTrial);
        Assert.True(plan.ExpiresNever);
    }

    [Fact]
    public async Task RequestsTheConfiguredProductFamilyByHandle()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"));
        var service = MaxioTestSupport.CreateService(handler);

        await service.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        var uri = request.RequestUri!.ToString();
        Assert.Contains("handle", uri);
        Assert.Contains("eshop-subscribe", uri);
    }
}
