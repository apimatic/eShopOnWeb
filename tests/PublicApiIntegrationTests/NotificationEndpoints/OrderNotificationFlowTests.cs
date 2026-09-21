using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    private const int SeededCatalogItemId = 1;
    private NotificationApiFactory _factory = null!;

    [TestInitialize]
    public void Init() => _factory = new NotificationApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient Shopper() => ClientFor(ApiTokenHelper.GetNormalUserToken());
    private HttpClient Operator() => ClientFor(ApiTokenHelper.GetAdminUserToken());

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task RegisterNumber(HttpClient client, string number = "+14165550100")
    {
        var r = await client.PostAsJsonAsync("api/contact-numbers", new { number });
        r.EnsureSuccessStatusCode();
    }

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = SeededCatalogItemId, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("orderId").GetInt32();
    }

    private static async Task<JsonElement> GetNotifications(HttpClient client, int orderId)
    {
        var r = await client.GetAsync($"api/orders/{orderId}/notifications");
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone().GetProperty("notifications");
    }

    [TestMethod]
    public async Task PlaceOrder_ReturnsOrderId_AndRecordsPlacedNotification()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);

        var orderId = await PlaceOrder(shopper);
        Assert.IsTrue(orderId > 0);

        var notifications = await GetNotifications(shopper, orderId);
        Assert.AreEqual(1, notifications.GetArrayLength());
        var n = notifications[0];
        Assert.AreEqual("OrderPlaced", n.GetProperty("kind").GetString());
        Assert.IsTrue(n.GetProperty("notificationId").GetInt32() > 0);
        Assert.IsFalse(string.IsNullOrEmpty(n.GetProperty("messageSid").GetString()));
        // GET refreshes live status from the provider; the fake reports delivered.
        Assert.AreEqual("delivered", n.GetProperty("channelStatus").GetString());
    }

    [TestMethod]
    public async Task PlaceOrder_WithNoNumberOnFile_IsNotMessaged()
    {
        var shopper = Shopper();
        var orderId = await PlaceOrder(shopper);

        var notifications = await GetNotifications(shopper, orderId);
        Assert.AreEqual(0, notifications.GetArrayLength());
    }

    [TestMethod]
    public async Task Dispatch_TellsShopper_AndQueuesFollowUp()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);

        var dispatch = await Operator().PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatch.StatusCode);

        var notifications = await GetNotifications(shopper, orderId);
        var kinds = notifications.EnumerateArray().Select(n => n.GetProperty("kind").GetString()).ToList();
        CollectionAssert.Contains(kinds, "OrderDispatched");
        CollectionAssert.Contains(kinds, "DeliveryFollowUp");

        var followUp = notifications.EnumerateArray().First(n => n.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.IsTrue(followUp.GetProperty("isScheduled").GetBoolean());
        Assert.IsFalse(string.IsNullOrEmpty(followUp.GetProperty("messageSid").GetString()));
    }

    [TestMethod]
    public async Task Cancel_CallsOffTheNotYetSentFollowUp()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);
        await Operator().PostAsync($"api/orders/{orderId}/dispatch", null);

        var cancel = await Operator().PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);

        var notifications = await GetNotifications(shopper, orderId);
        var kinds = notifications.EnumerateArray().Select(n => n.GetProperty("kind").GetString()).ToList();
        CollectionAssert.Contains(kinds, "OrderCancelled");

        var followUp = notifications.EnumerateArray().First(n => n.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", followUp.GetProperty("channelStatus").GetString());
    }

    [TestMethod]
    public async Task Dispatch_UnknownOrder_ReturnsNotFound()
    {
        var response = await Operator().PostAsync("api/orders/999999/dispatch", null);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Dispatch_AsNormalUser_IsForbidden()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);

        var response = await shopper.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderNotifications_OfAnotherShopper_AreNotVisible()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);

        var other = ClientFor(ApiTokenHelper.GetTokenForUser("other@microsoft.com"));
        var response = await other.GetAsync($"api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Resend_SameKeyDoesNotSendTwice_FreshKeyDoes()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);
        var placed = (await GetNotifications(shopper, orderId))[0].GetProperty("notificationId").GetInt32();

        var op = Operator();
        var first = await ResendWithKey(op, placed, "key-1");
        var repeat = await ResendWithKey(op, placed, "key-1");
        var fresh = await ResendWithKey(op, placed, "key-2");

        Assert.IsFalse(first.dedup, "first send should not be a duplicate");
        Assert.IsTrue(repeat.dedup, "repeat under same key must be a no-op");
        Assert.AreEqual(first.id, repeat.id, "repeat returns the same notification id");
        Assert.IsFalse(fresh.dedup, "fresh key is a legitimate second send");
        Assert.AreNotEqual(first.id, fresh.id, "fresh key produces a new notification");

        // Provider saw exactly: 1 (placed) + 1 (key-1) + 1 (key-2) = 3 sends.
        Assert.AreEqual(3, _factory.Gateway.Messages.Count);
    }

    private static async Task<(int id, bool dedup)> ResendWithKey(HttpClient client, int notificationId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/notifications/{notificationId}/resend");
        request.Headers.Add("Idempotency-Key", key);
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("notificationId").GetInt32(), doc.RootElement.GetProperty("deduplicated").GetBoolean());
    }

    [TestMethod]
    public async Task DisposeContent_RedactsAtProvider_ButTheRecordSurvives()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);
        var placed = (await GetNotifications(shopper, orderId))[0];
        var notificationId = placed.GetProperty("notificationId").GetInt32();
        var sid = placed.GetProperty("messageSid").GetString()!;

        var dispose = await Operator().DeleteAsync($"api/notifications/{notificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);

        // Redacted at the provider.
        Assert.IsTrue(_factory.Gateway.Messages[sid].Redacted);

        // The record and its outcome survive; content is marked disposed.
        var after = (await GetNotifications(shopper, orderId)).EnumerateArray()
            .First(n => n.GetProperty("notificationId").GetInt32() == notificationId);
        Assert.IsTrue(after.GetProperty("contentDisposed").GetBoolean());
        Assert.IsFalse(string.IsNullOrEmpty(after.GetProperty("channelStatus").GetString()));
    }

    [TestMethod]
    public async Task Reconciliation_LinesUpProviderAgainstEShop()
    {
        var shopper = Shopper();
        await RegisterNumber(shopper);
        var orderId = await PlaceOrder(shopper);
        await Operator().PostAsync($"api/orders/{orderId}/dispatch", null);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"));
        var response = await Operator().GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.AreEqual(_factory.Gateway.SendingNumber, root.GetProperty("fromNumber").GetString());
        // Placed + dispatched fall in the window and are known to both sides (the +3d follow-up is out of window).
        Assert.AreEqual(2, root.GetProperty("inBoth").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("providerOnly").GetArrayLength());
    }
}
