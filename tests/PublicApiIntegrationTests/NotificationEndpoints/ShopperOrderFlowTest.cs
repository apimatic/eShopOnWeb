using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Drives the shopper order flow offline (no contact number on file, so no Twilio call is made):
/// placing an order returns its id, the shopper can list their orders and an order's notifications,
/// and a shopper with no number is simply not messaged.
/// </summary>
[TestClass]
public class ShopperOrderFlowTest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient ShopperClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task PlaceOrder_WithNoNumberOnFile_SucceedsAndSendsNothing()
    {
        var client = ShopperClient();

        // A shopper with no number on file: listing returns an empty set.
        var listResponse = await client.GetAsync("api/contact-numbers");
        listResponse.EnsureSuccessStatusCode();

        // Place an order from a seeded catalog item.
        var orderJson = new StringContent(
            "{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}", Encoding.UTF8, "application/json");
        var placeResponse = await client.PostAsync("api/orders", orderJson);
        Assert.AreEqual(HttpStatusCode.Created, placeResponse.StatusCode);

        var placed = JsonSerializer.Deserialize<PlaceOrderResult>(
            await placeResponse.Content.ReadAsStringAsync(), Json);
        Assert.IsNotNull(placed);
        Assert.IsTrue(placed!.OrderId > 0);

        // The order shows up in my-orders with no notifications (nobody was messaged).
        var myOrders = await client.GetAsync("api/my-orders");
        myOrders.EnsureSuccessStatusCode();
        var body = await myOrders.Content.ReadAsStringAsync();
        StringAssert.Contains(body, $"\"orderId\":{placed.OrderId}");

        // The order's notifications endpoint is reachable by the owner and is empty.
        var notifications = await client.GetAsync($"api/orders/{placed.OrderId}/notifications");
        notifications.EnsureSuccessStatusCode();
        var notifBody = await notifications.Content.ReadAsStringAsync();
        StringAssert.Contains(notifBody, "\"notifications\":[]");
    }

    [TestMethod]
    public async Task PlaceOrder_WithEmptyItems_IsBadRequest()
    {
        var client = ShopperClient();
        var orderJson = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/orders", orderJson);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class PlaceOrderResult
    {
        public int OrderId { get; set; }
    }
}
