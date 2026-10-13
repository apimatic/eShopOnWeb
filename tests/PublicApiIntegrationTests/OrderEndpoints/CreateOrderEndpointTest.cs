using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private static async Task<int> CreateItemWithStockAsync(int stock)
    {
        var admin = AdminClient();
        var create = await admin.PostAsJsonAsync("api/catalog-items", new
        {
            catalogBrandId = 1,
            catalogTypeId = 2,
            description = "order test",
            name = "order test " + Guid.NewGuid(),
            price = 2.50m,
        });
        create.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("catalogItem").GetProperty("id").GetInt32();

        (await admin.PutAsJsonAsync($"api/catalog-items/{id}/stock", new { quantity = stock })).EnsureSuccessStatusCode();
        return id;
    }

    [TestMethod]
    public async Task Unauthenticated_IsUnauthorized()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Shopper_PlacesOrder_AndStockIsReduced()
    {
        var id = await CreateItemWithStockAsync(5);
        var shopper = ShopperClient();

        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = id, quantity = 3 } } });
        response.EnsureSuccessStatusCode();
        var orderId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();
        Assert.IsTrue(orderId > 0);

        // 2 left: a second order for 3 must be refused, proving the first order took 3 off eShop's stock.
        var second = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = id, quantity = 3 } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);

        // But a second order for the remaining 2 succeeds.
        var third = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = id, quantity = 2 } } });
        third.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task Order_ExceedingStock_IsRefused()
    {
        var id = await CreateItemWithStockAsync(1);

        var response = await ShopperClient().PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = id, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Order_WithNoItems_IsRejected()
    {
        var response = await ShopperClient().PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
