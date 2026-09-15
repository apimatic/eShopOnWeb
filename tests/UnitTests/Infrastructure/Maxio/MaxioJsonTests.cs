using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioJsonTests
{
    [Fact]
    public void SerializesCreateSubscriptionWithSpecFieldNames()
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshop:user:eshop-pro",
                PaymentCollectionMethod = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(payload, MaxioJson.SerializerOptions);

        Assert.Contains("\"product_handle\":\"eshop-pro\"", json);
        Assert.Contains("\"customer_id\":42", json);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", json);
        Assert.Contains("\"reference\":\"eshop:user:eshop-pro\"", json);
    }

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
                "product_family": { "handle": "eshop-subscribe" }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<ProductResponse>(json, MaxioJson.SerializerOptions);

        Assert.NotNull(parsed?.Product);
        Assert.Equal("eshop-pro", parsed!.Product!.Handle);
        Assert.Equal(29900, parsed.Product.PriceInCents);
        Assert.Equal("month", parsed.Product.IntervalUnit);
        Assert.Equal("eshop-subscribe", parsed.Product.ProductFamily?.Handle);
    }
}
