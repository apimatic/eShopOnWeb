using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderAndStockEndpointTest
{
    private static HttpClient AdminClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static HttpClient NormalClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static async Task<int> CreateCatalogItemAsync(HttpClient admin, string name)
    {
        var payload = JsonSerializer.Serialize(new
        {
            catalogBrandId = 1,
            catalogTypeId = 2,
            description = "stock test",
            name,
            price = 1.23m
        });
        var response = await admin.PostAsync("api/catalog-items", new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("catalogItem").GetProperty("id").GetInt32();
    }

    [TestMethod]
    public async Task SetCatalogStock_AsAdmin_UpdatesLevel()
    {
        var admin = AdminClient();
        var id = await CreateCatalogItemAsync(admin, "set-stock-item");

        var response = await admin.PutAsync($"api/catalog-items/{id}/stock",
            new StringContent("{\"quantity\":10}", Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(id, doc.RootElement.GetProperty("catalogItemId").GetInt32());
        Assert.AreEqual(10, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task SetCatalogStock_AsNormalUser_IsForbidden()
    {
        var normal = NormalClient();
        var response = await normal.PutAsync("api/catalog-items/1/stock",
            new StringContent("{\"quantity\":10}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_DecrementsStock_AndRefusesOverOrder()
    {
        var admin = AdminClient();
        var id = await CreateCatalogItemAsync(admin, "order-item");
        await admin.PutAsync($"api/catalog-items/{id}/stock",
            new StringContent("{\"quantity\":5}", Encoding.UTF8, "application/json"));

        // A signed-in shopper places an order within stock.
        var shopper = NormalClient();
        var orderBody = JsonSerializer.Serialize(new { items = new[] { new { catalogItemId = id, quantity = 3 } } });
        var orderResponse = await shopper.PostAsync("api/orders", new StringContent(orderBody, Encoding.UTF8, "application/json"));
        orderResponse.EnsureSuccessStatusCode();

        var json = await orderResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(json))
        {
            Assert.IsTrue(doc.RootElement.GetProperty("orderId").GetInt32() > 0);
        }

        // eShop stock dropped from 5 to 2; ordering more than the remaining 2 must be refused.
        var overBody = JsonSerializer.Serialize(new { items = new[] { new { catalogItemId = id, quantity = 3 } } });
        var overResponse = await shopper.PostAsync("api/orders", new StringContent(overBody, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, overResponse.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_Unauthenticated_IsUnauthorized()
    {
        var client = ProgramTest.NewClient;
        var orderBody = JsonSerializer.Serialize(new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var response = await client.PostAsync("api/orders", new StringContent(orderBody, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_UnknownItem_IsBadRequest()
    {
        var shopper = NormalClient();
        var orderBody = JsonSerializer.Serialize(new { items = new[] { new { catalogItemId = 999999, quantity = 1 } } });
        var response = await shopper.PostAsync("api/orders", new StringContent(orderBody, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
