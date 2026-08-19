using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioMappingTests
{
    [Fact]
    public void DeserializesProductResponseFromSpecShape()
    {
        const string json = """
            {
              "product": {
                "id": 7126957,
                "name": "Pro Plan",
                "handle": "eshop-pro",
                "description": "Monthly pro",
                "price_in_cents": 29900,
                "interval": 1,
                "interval_unit": "month",
                "archived_at": null,
                "require_credit_card": false,
                "product_family": {
                  "id": 3023074,
                  "name": "eShop Subscribe",
                  "handle": "eshop-subscribe"
                }
              }
            }
            """;

        var wrapped = JsonSerializer.Deserialize<ProductResponse>(json, MaxioJson.SerializerOptions);
        Assert.NotNull(wrapped?.Product);
        var plan = MaxioMapping.ToPlan(wrapped.Product);

        Assert.Equal(7126957, plan.Id);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public void DeserializesSubscriptionResponseFromSpecShape()
    {
        const string json = """
            {
              "subscription": {
                "id": 15236915,
                "state": "active",
                "reference": "user-1:eshop-pro",
                "product_price_in_cents": 29900,
                "current_period_ends_at": "2016-11-15T14:48:10-05:00",
                "next_assessment_at": "2016-11-15T14:48:10-05:00",
                "customer": { "id": 42, "email": "a@b.com" },
                "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
              }
            }
            """;

        var wrapped = JsonSerializer.Deserialize<SubscriptionResponse>(json, MaxioJson.SerializerOptions);
        Assert.NotNull(wrapped?.Subscription);
        var subscription = MaxioMapping.ToSubscription(wrapped.Subscription);

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(299.00m, subscription.Price);
        Assert.NotNull(subscription.NextBillingDate);
    }

    [Fact]
    public void FormatsArrayErrorListFromSpec()
    {
        var message = MaxioErrorFormatter.Format("""{"errors":["Bank routing number: cannot be blank."]}""");
        Assert.Contains("Bank routing number", message);
    }
}
