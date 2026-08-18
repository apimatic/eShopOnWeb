using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioJsonContractTests
{
    [Fact]
    public void CreateSubscriptionRequestMatchesOpenApiFieldNames()
    {
        var json = JsonSerializer.Serialize(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "42:eshop-pro",
                PaymentCollectionMethod = "remittance"
            }
        }, MaxioJson.Options);

        Assert.Contains("\"product_handle\":\"eshop-pro\"", json);
        Assert.Contains("\"customer_id\":42", json);
        Assert.Contains("\"reference\":\"42:eshop-pro\"", json);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", json);
        Assert.Contains("\"subscription\":", json);
    }

    [Fact]
    public void CreateCustomerRequestMatchesOpenApiFieldNames()
    {
        var json = JsonSerializer.Serialize(new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "Demo",
                LastName = "User",
                Email = "demouser@microsoft.com",
                Reference = "user-123"
            }
        }, MaxioJson.Options);

        Assert.Contains("\"first_name\":\"Demo\"", json);
        Assert.Contains("\"last_name\":\"User\"", json);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", json);
        Assert.Contains("\"reference\":\"user-123\"", json);
    }
}
