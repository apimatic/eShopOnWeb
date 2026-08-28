using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentAuthorizationTest
{
    [TestMethod]
    public async Task RequiresJwtForOrderCreation()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/orders", OrderRequest());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task HidesOrderFromAnotherShopperBeforeCallingPayPal()
    {
        var ownerClient = Client(ApiTokenHelper.GetNormalUserToken());
        var created = await ownerClient.PostAsJsonAsync("api/orders", OrderRequest());
        created.EnsureSuccessStatusCode();
        var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = createdJson.RootElement.GetProperty("orderId").GetInt32();

        var otherClient = Client(ApiTokenHelper.GetUserToken("another-shopper@example.test"));
        var payment = await otherClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = 12345 });
        Assert.AreEqual(HttpStatusCode.NotFound, payment.StatusCode);

        var myOrders = await otherClient.GetAsync("api/my-orders");
        myOrders.EnsureSuccessStatusCode();
        var ordersJson = JsonDocument.Parse(await myOrders.Content.ReadAsStringAsync());
        Assert.AreEqual(0, ordersJson.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task RestrictsOperatorRoutesToAdministratorRole()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var fulfil = await client.PostAsync("api/orders/1/fulfil", null);
        var cancel = await client.PostAsync("api/orders/1/cancel", null);
        var reconcile = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconcile.StatusCode);
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object OrderRequest() => new
    {
        items = new[] { new { catalogItemId = 1, quantity = 1 } },
        shipToAddress = new
        {
            street = "123 Main Street",
            city = "San Jose",
            state = "CA",
            country = "US",
            zipCode = "95131"
        }
    };
}
