using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SmsNotificationEndpoints;

[TestClass]
public class SmsNotificationFlowTests
{
    private SmsApiFactory _factory = null!;

    [TestInitialize]
    public void Init() => _factory = new SmsApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient ShopperClient() => Authed(ApiTokenHelper.GetNormalUserToken());
    private HttpClient AdminClient() => Authed(ApiTokenHelper.GetAdminUserToken());

    private HttpClient Authed(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<int> RegisterNumberAsync(HttpClient client, string number)
    {
        var response = await client.PostAsync("api/contact-numbers", Json(new { phoneNumber = number }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("contactNumberId").GetInt32();
    }

    private async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var response = await client.PostAsync("api/orders", Json(new { items = new[] { new { catalogItemId = 1, quantity = 2 } } }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("orderId").GetInt32();
    }

    private async Task<List<JsonElement>> NotificationsAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        return (await BodyAsync(response)).GetProperty("notifications").EnumerateArray().ToList();
    }

    [TestMethod]
    public async Task ContactNumbers_Register_List_Delete_Are_Owner_Scoped()
    {
        var shopper = ShopperClient();

        // Register — stores the provider's canonical form, returns the new id at the top level.
        var contactNumberId = await RegisterNumberAsync(shopper, "+1 (613) 555-0199");

        // The owner sees it.
        var list = await BodyAsync(await shopper.GetAsync("api/contact-numbers"));
        var owned = list.GetProperty("contactNumbers").EnumerateArray().ToList();
        Assert.AreEqual(1, owned.Count);
        Assert.AreEqual("+16135550199", owned[0].GetProperty("phoneNumber").GetString());

        // A different shopper (admin identity) does not.
        var otherList = await BodyAsync(await AdminClient().GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, otherList.GetProperty("contactNumbers").GetArrayLength());

        // Another shopper cannot delete it.
        Assert.AreEqual(HttpStatusCode.NotFound, (await AdminClient().DeleteAsync($"api/contact-numbers/{contactNumberId}")).StatusCode);

        // The owner can, and it is gone afterwards; a second delete is a 404.
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/contact-numbers/{contactNumberId}")).StatusCode);
        Assert.AreEqual(0, (await BodyAsync(await shopper.GetAsync("api/contact-numbers"))).GetProperty("contactNumbers").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/contact-numbers/{contactNumberId}")).StatusCode);
    }

    [TestMethod]
    public async Task Register_Rejects_Unusable_Number_At_Registration()
    {
        var response = await ShopperClient().PostAsync("api/contact-numbers", Json(new { phoneNumber = "invalid-number" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrder_Notifies_Shopper_And_Records_Notification()
    {
        var shopper = ShopperClient();
        await RegisterNumberAsync(shopper, "+16135550199");

        var orderId = await PlaceOrderAsync(shopper);

        var notifications = await NotificationsAsync(shopper, orderId);
        var placed = notifications.Single(n => n.GetProperty("type").GetString() == "OrderPlaced");
        Assert.IsFalse(string.IsNullOrEmpty(placed.GetProperty("providerMessageSid").GetString()));
        Assert.AreEqual(1, _factory.Provider.SendCount);
    }

    [TestMethod]
    public async Task PlaceOrder_With_No_Number_On_File_Sends_Nothing_But_Succeeds()
    {
        var shopper = ShopperClient();

        var orderId = await PlaceOrderAsync(shopper); // no number registered

        Assert.AreEqual(0, _factory.Provider.SendCount);
        Assert.AreEqual(0, (await NotificationsAsync(shopper, orderId)).Count);
    }

    [TestMethod]
    public async Task Dispatch_Is_Admin_Only_Schedules_FollowUp_And_Cancel_Calls_It_Off()
    {
        var shopper = ShopperClient();
        await RegisterNumberAsync(shopper, "+16135550199");
        var orderId = await PlaceOrderAsync(shopper);

        // A shopper cannot dispatch.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);

        // An operator can; a follow-up is queued with the provider.
        Assert.AreEqual(HttpStatusCode.OK, (await AdminClient().PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(1, _factory.Provider.ScheduleCount);

        var afterDispatch = await NotificationsAsync(shopper, orderId);
        Assert.IsTrue(afterDispatch.Any(n => n.GetProperty("type").GetString() == "OrderDispatched"));
        var followUp = afterDispatch.Single(n => n.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.IsTrue(followUp.GetProperty("isScheduled").GetBoolean());

        // Cancelling the order calls off the follow-up so it never goes out.
        Assert.AreEqual(HttpStatusCode.OK, (await AdminClient().PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        var afterCancel = await NotificationsAsync(shopper, orderId);
        var cancelledFollowUp = afterCancel.Single(n => n.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", cancelledFollowUp.GetProperty("status").GetString());
        Assert.IsTrue(afterCancel.Any(n => n.GetProperty("type").GetString() == "OrderCancelled"));
    }

    [TestMethod]
    public async Task Resend_Is_Idempotent_Per_Key()
    {
        var shopper = ShopperClient();
        await RegisterNumberAsync(shopper, "+16135550199");
        var orderId = await PlaceOrderAsync(shopper);
        var sendsAfterPlace = _factory.Provider.SendCount;

        var notificationId = (await NotificationsAsync(shopper, orderId))
            .First(n => n.GetProperty("type").GetString() == "OrderPlaced")
            .GetProperty("notificationId").GetInt32();

        var admin = AdminClient();

        // First attempt under key "k1" sends and returns a new notification id.
        var first = await BodyAsync(await admin.PostAsync($"api/notifications/{notificationId}/resend", Json(new { idempotencyKey = "k1" })));
        var firstId = first.GetProperty("notificationId").GetInt32();
        Assert.IsFalse(first.GetProperty("reused").GetBoolean());

        // Repeat under the SAME key: no second message, same id returned.
        var repeat = await BodyAsync(await admin.PostAsync($"api/notifications/{notificationId}/resend", Json(new { idempotencyKey = "k1" })));
        Assert.AreEqual(firstId, repeat.GetProperty("notificationId").GetInt32());
        Assert.IsTrue(repeat.GetProperty("reused").GetBoolean());

        // A genuine new attempt under a fresh key sends again.
        var second = await BodyAsync(await admin.PostAsync($"api/notifications/{notificationId}/resend", Json(new { idempotencyKey = "k2" })));
        Assert.AreNotEqual(firstId, second.GetProperty("notificationId").GetInt32());
        Assert.IsFalse(second.GetProperty("reused").GetBoolean());

        // Exactly two extra sends (k1 once, k2 once) — never three.
        Assert.AreEqual(sendsAfterPlace + 2, _factory.Provider.SendCount);
    }

    [TestMethod]
    public async Task DisposeContent_Redacts_Body_Locally_And_At_Provider()
    {
        var shopper = ShopperClient();
        await RegisterNumberAsync(shopper, "+16135550199");
        var orderId = await PlaceOrderAsync(shopper);

        var placed = (await NotificationsAsync(shopper, orderId)).Single(n => n.GetProperty("type").GetString() == "OrderPlaced");
        var notificationId = placed.GetProperty("notificationId").GetInt32();
        var sid = placed.GetProperty("providerMessageSid").GetString()!;

        Assert.AreEqual(HttpStatusCode.NoContent, (await AdminClient().DeleteAsync($"api/notifications/{notificationId}/content")).StatusCode);

        var afterDisposal = (await NotificationsAsync(shopper, orderId)).Single(n => n.GetProperty("notificationId").GetInt32() == notificationId);
        Assert.IsTrue(afterDisposal.GetProperty("contentRedacted").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, afterDisposal.GetProperty("messageBody").ValueKind);

        // The fact and outcome survive at the provider; the body was redacted there too.
        Assert.IsTrue(_factory.Provider.Get(sid)!.Redacted);
    }

    [TestMethod]
    public async Task Reconciliation_Lists_Provider_Messages_For_The_Sending_Number()
    {
        var shopper = ShopperClient();
        await RegisterNumberAsync(shopper, "+16135550199");
        await PlaceOrderAsync(shopper);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"));

        var report = await BodyAsync(await AdminClient().GetAsync($"api/notifications/reconciliation?from={from}&to={to}"));
        Assert.AreEqual(_factory.Provider.SendingNumber, report.GetProperty("fromNumber").GetString());
        Assert.IsTrue(report.GetProperty("inBothCount").GetInt32() >= 1);
        Assert.IsTrue(report.GetProperty("entries").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task Reconciliation_Requires_Administrator()
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"));
        var response = await ShopperClient().GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
