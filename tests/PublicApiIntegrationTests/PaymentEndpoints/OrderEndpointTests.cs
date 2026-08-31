using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class OrderEndpointTests
{
    private const string RequestJson = """
        {
          "items": [{ "catalogItemId": 1, "quantity": 1 }],
          "shippingAddress": {
            "street": "1 Test St", "city": "Seattle", "state": "WA",
            "country": "United States", "zipCode": "98101"
          }
        }
        """;

    [TestMethod]
    public async Task RequiresBearerToken()
    {
        using var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders",
            new StringContent(RequestJson, Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatesExistingOrderAggregateAndScopesItToShopper()
    {
        using var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsync("api/orders",
            new StringContent(RequestJson, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var orderId = createdJson.GetProperty("orderId").GetInt32();
        Assert.AreEqual("AwaitingPayment", createdJson.GetProperty("paymentStatus").GetString());

        using var otherShopper = ProgramTest.NewClient;
        otherShopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var otherOrders = JsonDocument.Parse(await otherShopper.GetStringAsync("api/my-orders"));
        Assert.IsFalse(otherOrders.RootElement.EnumerateArray()
            .Any(x => x.GetProperty("orderId").GetInt32() == orderId));

        var forbidden = await shopper.PostAsync($"api/orders/{orderId}/fulfil",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
