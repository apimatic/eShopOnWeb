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
    private static HttpClient Authed(WalmartApiFactory f, string token)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task Order_Unauthenticated_Unauthorized()
    {
        using var f = new WalmartApiFactory();
        var response = await f.CreateClient().PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Order_WithinStock_SucceedsAndDecrementsEshopStock()
    {
        using var f = new WalmartApiFactory();
        var admin = Authed(f, ApiTokenHelper.GetAdminUserToken());
        var shopper = Authed(f, ApiTokenHelper.GetNormalUserToken());

        // Admin sets eShop stock for item 1 to 10.
        (await admin.PutAsJsonAsync("api/catalog-items/1/stock", new { quantity = 10 })).EnsureSuccessStatusCode();

        // Shopper orders 3 -> success, order id returned.
        var order = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 3 } } });
        order.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("orderId").GetInt32() > 0);

        // 7 remain; ordering 8 now must be refused.
        var tooMany = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 8 } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);

        // Ordering the remaining 7 succeeds.
        var rest = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 7 } } });
        rest.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task Order_ExceedingStock_Refused()
    {
        using var f = new WalmartApiFactory();
        var admin = Authed(f, ApiTokenHelper.GetAdminUserToken());
        var shopper = Authed(f, ApiTokenHelper.GetNormalUserToken());

        (await admin.PutAsJsonAsync("api/catalog-items/2/stock", new { quantity = 1 })).EnsureSuccessStatusCode();

        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 5 } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Order_ItemNeverStocked_RefusedAsZeroStock()
    {
        using var f = new WalmartApiFactory();
        var shopper = Authed(f, ApiTokenHelper.GetNormalUserToken());

        // Item 3 stock was never set -> treated as 0; any order is refused.
        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 3, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateCatalogStock_NormalUser_Forbidden()
    {
        using var f = new WalmartApiFactory();
        var shopper = Authed(f, ApiTokenHelper.GetNormalUserToken());
        var response = await shopper.PutAsJsonAsync("api/catalog-items/1/stock", new { quantity = 10 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateCatalogStock_Admin_EchoesQuantity()
    {
        using var f = new WalmartApiFactory();
        var admin = Authed(f, ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PutAsJsonAsync("api/catalog-items/1/stock", new { quantity = 42 });
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, doc.RootElement.GetProperty("catalogItemId").GetInt32());
        Assert.AreEqual(42, doc.RootElement.GetProperty("quantity").GetInt32());
    }
}
