using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderEndpointTests
{
    [TestMethod]
    public async Task PlaceOrderRequiresAuth()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders", CreateOrderJson());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrderAndListMine()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var create = await client.PostAsync("api/orders", CreateOrderJson());
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();
        Assert.IsNotNull(created);
        Assert.IsTrue(created!.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", created.Order.Status);

        var mine = await client.GetAsync("api/my-orders");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<GetMyOrdersResponse>();
        Assert.IsNotNull(list);
        Assert.IsTrue(list!.Orders.Exists(o => o.OrderId == created.OrderId));
    }

    [TestMethod]
    public async Task ShopperCannotSeeAnotherShoppersOrders()
    {
        var demo = ProgramTest.NewClient;
        demo.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var create = await demo.PostAsync("api/orders", CreateOrderJson());
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        var other = ProgramTest.NewClient;
        other.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetToken("other@microsoft.com"));
        var mine = await other.GetAsync("api/my-orders");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<GetMyOrdersResponse>();
        Assert.IsFalse(list!.Orders.Exists(o => o.OrderId == created!.OrderId));
    }

    [TestMethod]
    public async Task FulfilAndCancelAndReconciliationRequireAdmin()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var fulfil = await client.PostAsync("api/orders/1/fulfil", new StringContent("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);

        var cancel = await client.PostAsync("api/orders/1/cancel", new StringContent("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);

        var recon = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, recon.StatusCode);
    }

    private static StringContent CreateOrderJson()
    {
        var request = new CreateOrderRequest
        {
            Items = new()
            {
                new CreateOrderItemRequest { CatalogItemId = 2, Quantity = 1 }
            }
        };
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }
}
