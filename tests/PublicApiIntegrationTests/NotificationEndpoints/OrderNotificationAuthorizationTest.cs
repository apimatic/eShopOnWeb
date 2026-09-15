using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Authorization + "a messaging failure never fails the underlying operation" for the SMS
/// notification flow. These tests use no real Twilio account: a shopper with no number on file is
/// simply not messaged, so placing/dispatching/cancelling never touches the provider.
/// </summary>
[TestClass]
public class OrderNotificationAuthorizationTest
{
    private static HttpClient AuthenticatedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ContactNumbers_Get_Requires_Authentication()
    {
        var client = ProgramTest.NewClient; // no token
        var response = await client.GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Dispatch_Is_Forbidden_For_A_Normal_User()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/dispatch", content: null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_Is_Forbidden_For_A_Normal_User()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/notifications/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Resend_Is_Forbidden_For_A_Normal_User()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/notifications/1/resend", Json(new { idempotencyKey = "k1" }));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_Succeeds_Without_A_Number_And_Appears_In_MyOrders()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        // The shopper has no number on file, so no message is attempted — the order is still placed.
        var placeResponse = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = 1, quantity = 2 } } }));
        placeResponse.EnsureSuccessStatusCode();
        var placed = (await placeResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();
        Assert.IsNotNull(placed);
        Assert.IsTrue(placed!.OrderId > 0);

        var myOrdersResponse = await client.GetAsync("api/my-orders");
        myOrdersResponse.EnsureSuccessStatusCode();
        var myOrders = (await myOrdersResponse.Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>();
        Assert.IsNotNull(myOrders);
        Assert.IsTrue(myOrders!.Orders.Exists(o => o.OrderId == placed.OrderId));
    }

    [TestMethod]
    public async Task Admin_Can_Dispatch_Then_Cancel_A_Placed_Order()
    {
        // A shopper places an order (no number on file → no provider call).
        var shopper = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var placeResponse = await shopper.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = 2, quantity = 1 } } }));
        placeResponse.EnsureSuccessStatusCode();
        var placed = (await placeResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();
        Assert.IsNotNull(placed);

        var admin = AuthenticatedClient(ApiTokenHelper.GetAdminUserToken());

        var dispatchResponse = await admin.PostAsync($"api/orders/{placed!.OrderId}/dispatch", content: null);
        dispatchResponse.EnsureSuccessStatusCode();
        var dispatched = (await dispatchResponse.Content.ReadAsStringAsync()).FromJson<OrderTransitionResponse>();
        Assert.AreEqual("Dispatched", dispatched!.Status);

        var cancelResponse = await admin.PostAsync($"api/orders/{placed.OrderId}/cancel", content: null);
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadAsStringAsync()).FromJson<OrderTransitionResponse>();
        Assert.AreEqual("Cancelled", cancelled!.Status);

        // Dispatching a cancelled order is rejected as a conflict.
        var reDispatch = await admin.PostAsync($"api/orders/{placed.OrderId}/dispatch", content: null);
        Assert.AreEqual(HttpStatusCode.Conflict, reDispatch.StatusCode);
    }
}
