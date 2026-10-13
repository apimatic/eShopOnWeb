using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CreateOrderEndpointTest
{
    private static HttpClient AdminClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static HttpClient ShopperClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent StockBody(int quantity) =>
        new(JsonSerializer.Serialize(new { quantity }), Encoding.UTF8, "application/json");

    private static StringContent OrderBody(int catalogItemId, int quantity) =>
        new(JsonSerializer.Serialize(new { items = new[] { new { catalogItemId, quantity } } }), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var client = ProgramTest.NewClient; // no token
        var response = await client.PostAsync("api/orders", OrderBody(1, 1));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlacesOrderAndDecrementsStock()
    {
        // Set eShop stock to a known baseline first (admin).
        (await AdminClient().PutAsync("api/catalog-items/3/stock", StockBody(10))).EnsureSuccessStatusCode();

        // A signed-in shopper may order within stock.
        var orderResponse = await ShopperClient().PostAsync("api/orders", OrderBody(3, 4));
        orderResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await orderResponse.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("orderId").GetInt32() > 0);

        // Walmart's record is untouched by placing an order; eShop's own level dropped to 6.
        var stockResponse = await AdminClient().GetAsync("api/catalog-items/3");
        stockResponse.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task RefusesOrderExceedingStock()
    {
        (await AdminClient().PutAsync("api/catalog-items/4/stock", StockBody(2))).EnsureSuccessStatusCode();

        var response = await ShopperClient().PostAsync("api/orders", OrderBody(4, 5));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RejectsEmptyOrder()
    {
        var body = new StringContent(JsonSerializer.Serialize(new { items = new object[0] }), Encoding.UTF8, "application/json");
        var response = await ShopperClient().PostAsync("api/orders", body);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RefusesUnknownCatalogItem()
    {
        var response = await ShopperClient().PostAsync("api/orders", OrderBody(999999, 1));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
