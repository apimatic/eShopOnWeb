using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class OrderEndpointAuthorizationTests
{
    private static StringContent OrderBody() => new(JsonSerializer.Serialize(new
    {
        items = new[] { new { catalogItemId = 1, quantity = 1 } },
        shippingAddress = new
        {
            street = "123 Main Street",
            city = "San Jose",
            state = "CA",
            country = "US",
            zipCode = "95131"
        }
    }), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task PlaceOrderRequiresBearerToken()
    {
        using var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsync("api/orders", OrderBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrderReturnsTopLevelIdAndMyOrdersIsShopperScoped()
    {
        using var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var create = await shopper.PostAsync("api/orders", OrderBody());
        create.EnsureSuccessStatusCode();
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());

        Assert.IsTrue(createdJson.RootElement.GetProperty("orderId").GetInt32() > 0);
        Assert.AreEqual("AwaitingPayment", createdJson.RootElement.GetProperty("status").GetString());

        using var administrator = ProgramTest.NewClient;
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var adminOrders = await administrator.GetAsync("api/my-orders");
        adminOrders.EnsureSuccessStatusCode();
        using var adminJson = JsonDocument.Parse(await adminOrders.Content.ReadAsStringAsync());
        Assert.AreEqual(0, adminJson.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task FulfilAndReconciliationRequireAdministratorRole()
    {
        using var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var fulfil = await shopper.PostAsync("api/orders/1/fulfil", new StringContent("{}", Encoding.UTF8,
            "application/json"));
        var reconciliation = await shopper.GetAsync(
            "api/reconciliation?from=2026-08-01T00%3A00%3A00Z&to=2026-08-02T00%3A00%3A00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }
}
