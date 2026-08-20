using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioContractsTests
{
    [Fact]
    public void CreateCustomerRequest_SerializesToSpecShape()
    {
        var json = JsonSerializer.Serialize(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = "Demo",
                LastName = "Shopper",
                Email = "demo@example.com",
                Reference = "user-1"
            }
        }, MaxioJson.SerializerOptions);

        Assert.Contains("\"customer\":", json);
        Assert.Contains("\"first_name\":\"Demo\"", json);
        Assert.Contains("\"last_name\":\"Shopper\"", json);
        Assert.Contains("\"email\":\"demo@example.com\"", json);
        Assert.Contains("\"reference\":\"user-1\"", json);
    }

    [Fact]
    public void ProductResponse_DeserializesSpecWrapper()
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
                "require_credit_card": false,
                "archived_at": null,
                "product_family": { "handle": "eshop-subscribe", "name": "eShop" }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<MaxioProductResponse>(json, MaxioJson.SerializerOptions);

        Assert.NotNull(parsed?.Product);
        Assert.Equal("eshop-pro", parsed.Product.Handle);
        Assert.Equal(29900, parsed.Product.PriceInCents);
        Assert.Equal("month", parsed.Product.IntervalUnit);
        Assert.False(parsed.Product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", parsed.Product.ProductFamily?.Handle);
    }

    [Fact]
    public void SubscriptionResponse_DeserializesNextAssessmentAt()
    {
        const string json = """
            {
              "subscription": {
                "id": 42,
                "state": "active",
                "product_price_in_cents": 2900,
                "next_assessment_at": "2026-09-21T12:00:00-04:00",
                "current_period_ends_at": "2026-09-21T12:00:00-04:00",
                "reference": "user-1:basic-plan",
                "product": { "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(json, MaxioJson.SerializerOptions);

        Assert.NotNull(parsed?.Subscription);
        Assert.Equal("active", parsed.Subscription.State);
        Assert.Equal("basic-plan", parsed.Subscription.Product?.Handle);
        Assert.NotNull(parsed.Subscription.NextAssessmentAt);
    }
}
