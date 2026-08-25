using System.Linq;
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
public class MyOrdersEndpointTest
{
    [TestMethod]
    public async Task ReturnsUnauthorizedWithNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task OnlyShowsCallersOwnOrders()
    {
        var client = ProgramTest.NewClient;

        // Place an order as the normal user.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var placeRequest = new { Items = new[] { new { CatalogItemId = 2, Quantity = 1 } } };
        var placeContent = new StringContent(JsonSerializer.Serialize(placeRequest), Encoding.UTF8, "application/json");
        var placeResponse = await client.PostAsync("api/orders", placeContent);
        placeResponse.EnsureSuccessStatusCode();
        var placed = (await placeResponse.Content.ReadAsStringAsync()).FromJson<PlaceOrderResponse>()!;

        // The same shopper sees the order in their own list.
        var myOrdersResponse = await client.GetAsync("api/my-orders");
        myOrdersResponse.EnsureSuccessStatusCode();
        var myOrders = (await myOrdersResponse.Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>()!;
        Assert.IsTrue(myOrders.Orders.Any(o => o.OrderId == placed.OrderId));

        // A different shopper (the admin account) does not see it in theirs.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        var adminOrdersResponse = await client.GetAsync("api/my-orders");
        adminOrdersResponse.EnsureSuccessStatusCode();
        var adminOrders = (await adminOrdersResponse.Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>()!;
        Assert.IsFalse(adminOrders.Orders.Any(o => o.OrderId == placed.OrderId));
    }
}
