using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class OrderEndpointTests
{
    private WalmartApiFactory _factory = null!;

    [TestInitialize]
    public void Init() => _factory = new WalmartApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task SetStockAsync(int catalogItemId, int quantity)
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var resp = await admin.PutAsync($"api/catalog-items/{catalogItemId}/stock", Json($$"""{ "quantity": {{quantity}} }"""));
        resp.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task Shopper_PlacesOrder_AndStockIsTakenOff()
    {
        await SetStockAsync(3, 5);
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var resp = await shopper.PostAsync("api/orders", Json("""{ "items": [ { "catalogItemId": 3, "quantity": 5 } ] }"""));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("orderId").GetInt32() > 0);

        // Stock is now 0 — a further order for the same item is refused.
        var again = await shopper.PostAsync("api/orders", Json("""{ "items": [ { "catalogItemId": 3, "quantity": 1 } ] }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [TestMethod]
    public async Task Order_ForMoreThanHeld_IsRefused()
    {
        await SetStockAsync(4, 2);
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var resp = await shopper.PostAsync("api/orders", Json("""{ "items": [ { "catalogItemId": 4, "quantity": 3 } ] }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Order_UnknownItem_IsBadRequest()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var resp = await shopper.PostAsync("api/orders", Json("""{ "items": [ { "catalogItemId": 999999, "quantity": 1 } ] }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Order_NoToken_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("api/orders", Json("""{ "items": [ { "catalogItemId": 3, "quantity": 1 } ] }"""));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
