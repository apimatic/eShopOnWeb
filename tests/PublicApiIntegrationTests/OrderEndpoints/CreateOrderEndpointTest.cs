using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Walmart;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CreateOrderEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient ShopperClient(WalmartTestApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static HttpClient AdminClient(WalmartTestApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    [TestMethod]
    public async Task PlaceOrder_DeductsEShopStock_AndReturnsOrderId()
    {
        using var factory = new WalmartTestApplicationFactory();
        var admin = AdminClient(factory);
        var shopper = ShopperClient(factory);

        (await admin.PutAsJsonAsync("api/catalog-items/5/stock", new { quantity = 20 })).EnsureSuccessStatusCode();

        var order = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 5, quantity = 3 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, order.StatusCode);
        var orderBody = await order.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.IsTrue(orderBody!.OrderId > 0);

        // eShop stock reduced 20 -> 17, visible via the reconciliation report (eshopQuantity).
        var report = await admin.GetAsync("api/walmart/stock");
        report.EnsureSuccessStatusCode();
        var reportBody = await report.Content.ReadFromJsonAsync<ReportResponse>(JsonOptions);
        Assert.AreEqual(17, reportBody!.Items.Single(i => i.Sku == "ESHOP-5").EshopQuantity);
    }

    [TestMethod]
    public async Task PlaceOrder_ForMoreThanHeld_IsRefused()
    {
        using var factory = new WalmartTestApplicationFactory();
        var admin = AdminClient(factory);
        var shopper = ShopperClient(factory);

        (await admin.PutAsJsonAsync("api/catalog-items/6/stock", new { quantity = 2 })).EnsureSuccessStatusCode();

        var order = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 6, quantity = 5 } }
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, order.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_RequiresAuthentication()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = factory.CreateClient();

        var order = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 5, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Unauthorized, order.StatusCode);
    }

    private sealed class OrderResponse
    {
        public int OrderId { get; set; }
    }

    private sealed class ReportResponse
    {
        public System.Collections.Generic.List<ReportItem> Items { get; set; } = new();
    }

    private sealed class ReportItem
    {
        public string Sku { get; set; } = string.Empty;
        public int? WalmartQuantity { get; set; }
        public int? EshopQuantity { get; set; }
    }
}
