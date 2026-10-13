using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// Placing an order takes the ordered quantities off eShop's stock, and an order for more of an
/// item than eShop holds is refused. Available to any signed-in shopper. End-to-end through
/// PublicApi alone (create item + set stock as admin, order as a normal user).
/// </summary>
[TestClass]
public class CreateOrderEndpointTest
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient AdminClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static HttpClient NormalClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<int> CreateCatalogItemAsync()
    {
        var uniqueName = "stock-test-item-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        var body = Json($"{{\"catalogBrandId\":1,\"catalogTypeId\":2,\"description\":\"d\",\"name\":\"{uniqueName}\",\"price\":1.5}}");
        var response = await AdminClient().PostAsync("api/catalog-items", body);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("catalogItem").GetProperty("id").GetInt32();
    }

    private static async Task SetStockAsync(int catalogItemId, int quantity)
    {
        var response = await AdminClient().PutAsync($"api/catalog-items/{catalogItemId}/stock", Json($"{{\"quantity\":{quantity}}}"));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(quantity, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    private static Task<HttpResponseMessage> OrderAsync(int catalogItemId, int quantity) =>
        NormalClient().PostAsync("api/orders", Json($"{{\"items\":[{{\"catalogItemId\":{catalogItemId},\"quantity\":{quantity}}}]}}"));

    [TestMethod]
    public async Task OrderSucceedsAndDecrementsStock()
    {
        var itemId = await CreateCatalogItemAsync();
        await SetStockAsync(itemId, 10);

        // Order 3 of 10 -> success, returns an orderId.
        var ok = await OrderAsync(itemId, 3);
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        using (var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            Assert.IsTrue(doc.RootElement.GetProperty("orderId").GetInt32() > 0);
        }

        // Only 7 left: ordering 8 must be refused...
        var tooMany = await OrderAsync(itemId, 8);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);

        // ...but ordering the remaining 7 succeeds, proving the first order decremented stock.
        var rest = await OrderAsync(itemId, 7);
        Assert.AreEqual(HttpStatusCode.Created, rest.StatusCode);
    }

    [TestMethod]
    public async Task OrderForMoreThanHeldIsRefused()
    {
        var itemId = await CreateCatalogItemAsync();
        await SetStockAsync(itemId, 2);

        var response = await OrderAsync(itemId, 5);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderRequiresAuthentication()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderWithUnknownItemIsRejected()
    {
        var response = await OrderAsync(catalogItemId: 999999, quantity: 1);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
