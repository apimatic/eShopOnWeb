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
public class OrderEndpointTest
{
    private const string OrderJson = """
        {
          "items": [{ "catalogItemId": 1, "quantity": 2 }],
          "shippingAddress": {
            "street": "1 Test Way", "city": "Seattle", "state": "WA",
            "country": "United States", "zipCode": "98101"
          }
        }
        """;

    [TestMethod]
    public async Task RequiresBearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/orders", Content(OrderJson));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatesOrderWithTopLevelIdentifierAndScopesItToShopper()
    {
        var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsync("api/orders", Content(OrderJson));
        created.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("orderId").GetInt32();

        var admin = ProgramTest.NewClient;
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        var adminOrders = await admin.GetAsync("api/my-orders");
        adminOrders.EnsureSuccessStatusCode();
        using var adminBody = JsonDocument.Parse(await adminOrders.Content.ReadAsStringAsync());

        Assert.IsFalse(adminBody.RootElement.EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task FulfilRequiresAdministratorRole()
    {
        var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsync("api/orders", Content(OrderJson));
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("orderId").GetInt32();

        var response = await shopper.PostAsync($"api/orders/{orderId}/fulfil", Content("{}"));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/json");
}
