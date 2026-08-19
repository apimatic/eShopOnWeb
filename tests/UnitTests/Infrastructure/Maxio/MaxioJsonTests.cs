using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioJsonTests
{
    [Fact]
    public void CreateSubscriptionRequest_SerializesToChargifySnakeCase()
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = "eshop-pro",
                CustomerId = 11,
                CustomerReference = "user-1",
                Reference = "user-1:eshop-pro"
            }
        };

        var json = JsonSerializer.Serialize(payload, MaxioJson.Options);

        Assert.Contains("\"product_handle\":\"eshop-pro\"", json);
        Assert.Contains("\"customer_id\":11", json);
        Assert.Contains("\"customer_reference\":\"user-1\"", json);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", json);
    }

    [Fact]
    public void ProductEnvelope_DeserializesChargifyWrapper()
    {
        const string json = """
            [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
            """;

        var envelopes = JsonSerializer.Deserialize<List<ProductEnvelope>>(json, MaxioJson.Options);

        Assert.NotNull(envelopes);
        Assert.Single(envelopes);
        Assert.Equal("eshop-pro", envelopes![0].Product!.Handle);
        Assert.Equal(29900, envelopes[0].Product!.PriceInCents);
    }
}
