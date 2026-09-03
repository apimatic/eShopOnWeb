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
    [TestMethod]
    public async Task PaymentMethodsRequireBearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanPlaceAndReadAwaitingPaymentOrder()
    {
        var client = ShopperClient();
        var body = JsonSerializer.Serialize(new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Street", city = "Seattle", state = "WA", country = "US", zipCode = "98101"
            }
        });

        var created = await client.PostAsync("api/orders", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.IsTrue(createdJson.RootElement.GetProperty("orderId").GetInt32() > 0);
        Assert.AreEqual("AwaitingPayment",
            createdJson.RootElement.GetProperty("order").GetProperty("paymentStatus").GetString());

        var mine = await client.GetAsync("api/my-orders");
        mine.EnsureSuccessStatusCode();
        using var ordersJson = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        Assert.IsTrue(ordersJson.RootElement.GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task ReconciliationRejectsShopperRole()
    {
        var response = await ShopperClient().GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpClient ShopperClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
