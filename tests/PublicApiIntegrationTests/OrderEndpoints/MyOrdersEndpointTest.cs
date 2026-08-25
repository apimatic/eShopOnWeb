using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class MyOrdersEndpointTest
{
    private static async Task<int> CreateOrderAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new CreateOrderRequest
        {
            Items = new List<OrderItemLineRequest> { new() { CatalogItemId = 2, Quantity = 1 } }
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/orders", content);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();
        return body!.OrderId;
    }

    [TestMethod]
    public async Task ShopperSeesTheirOwnOrderInMyOrders()
    {
        var owner = "order-owner@microsoft.com";
        var createClient = ProgramTest.NewClient;
        var orderId = await CreateOrderAsync(createClient, ApiTokenHelper.GetUserToken(owner));

        var listClient = ProgramTest.NewClient;
        listClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(owner));
        var listResponse = await listClient.GetAsync("api/my-orders");
        listResponse.EnsureSuccessStatusCode();
        var body = (await listResponse.Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>();

        Assert.IsTrue(body!.Orders.Any(o => o.OrderId == orderId));
    }

    [TestMethod]
    public async Task ShopperDoesNotSeeAnotherShoppersOrder()
    {
        var owner = "order-owner-2@microsoft.com";
        var otherUser = "order-stranger@microsoft.com";

        var createClient = ProgramTest.NewClient;
        var orderId = await CreateOrderAsync(createClient, ApiTokenHelper.GetUserToken(owner));

        var listClient = ProgramTest.NewClient;
        listClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(otherUser));
        var listResponse = await listClient.GetAsync("api/my-orders");
        listResponse.EnsureSuccessStatusCode();
        var body = (await listResponse.Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>();

        Assert.IsFalse(body!.Orders.Any(o => o.OrderId == orderId));
    }
}
