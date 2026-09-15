using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioJsonTests
{
    [Fact]
    public void SerializesCreateCustomerRequestWithSpecFieldNames()
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = "Demo",
                LastName = "User",
                Email = "demouser@microsoft.com",
                Reference = "demouser@microsoft.com"
            }
        };

        var json = JsonSerializer.Serialize(request, MaxioJson.SerializerOptions);

        Assert.Contains("\"customer\":", json);
        Assert.Contains("\"first_name\":\"Demo\"", json);
        Assert.Contains("\"last_name\":\"User\"", json);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", json);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", json);
    }

    [Fact]
    public void DeserializesSubscriptionResponseFromSpecShape()
    {
        const string json = """
            {
              "subscription": {
                "id": 15236915,
                "state": "active",
                "product_price_in_cents": 29900,
                "next_assessment_at": "2026-09-19T00:00:00+00:00",
                "product": {
                  "id": 7126957,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "price_in_cents": 29900
                }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<SubscriptionResponse>(json, MaxioJson.SerializerOptions);

        Assert.NotNull(parsed?.Subscription);
        Assert.Equal(15236915, parsed!.Subscription!.Id);
        Assert.Equal("active", parsed.Subscription.State);
        Assert.Equal(29900, parsed.Subscription.ProductPriceInCents);
        Assert.Equal("eshop-pro", parsed.Subscription.Product?.Handle);
    }
}
