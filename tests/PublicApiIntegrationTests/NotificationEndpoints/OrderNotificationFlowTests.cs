using System;
using System.Collections.Generic;
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
    private NotificationTestFactory _factory = default!;

    // A safe-to-use fictitious number with 10+ digits so the fake validator accepts it.
    private const string ShopperNumber = "+15005550001";

    [TestInitialize]
    public void Init() => _factory = new NotificationTestFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone();
    }

    // ---------- Flow 1: contact numbers ----------

    [TestMethod]
    public async Task Register_valid_number_returns_contactNumberId_and_stores_canonical()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        // The caller types a messy, formatted value; the provider's canonical E.164 form is what is stored.
        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "+1 (500) 555-0001" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var json = await ReadJson(response);
        Assert.IsTrue(json.GetProperty("contactNumberId").GetInt32() > 0);
        Assert.AreEqual("+15005550001", json.GetProperty("phoneNumber").GetString());
    }

    [TestMethod]
    public async Task Register_unusable_number_is_rejected_with_400()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "123" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Numbers_are_owner_scoped_and_deletable()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var other = Client(ApiTokenHelper.GetAdminUserToken());

        var created = await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });
        var id = (await ReadJson(created)).GetProperty("contactNumberId").GetInt32();

        // The other user cannot see it.
        var otherList = await ReadJson(await other.GetAsync("api/contact-numbers"));
        Assert.IsFalse(otherList.GetProperty("contactNumbers").EnumerateArray()
            .Any(n => n.GetProperty("contactNumberId").GetInt32() == id));

        // The other user cannot delete it.
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/contact-numbers/{id}")).StatusCode);

        // The owner can, and afterwards it is gone.
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.DeleteAsync($"api/contact-numbers/{id}")).StatusCode);
        var ownerList = await ReadJson(await shopper.GetAsync("api/contact-numbers"));
        Assert.IsFalse(ownerList.GetProperty("contactNumbers").EnumerateArray()
            .Any(n => n.GetProperty("contactNumberId").GetInt32() == id));
    }

    // ---------- Flow 2: messages as the order moves ----------

    [TestMethod]
    public async Task Placing_an_order_notifies_the_shopper()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });

        var orderId = await PlaceOrder(shopper);

        var notifications = await GetNotifications(shopper, orderId);
        Assert.AreEqual(1, notifications.Count);
        Assert.AreEqual("OrderPlaced", notifications[0].GetProperty("kind").GetString());
        Assert.AreEqual("Sent", notifications[0].GetProperty("state").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(notifications[0].GetProperty("providerMessageSid").GetString()));
    }

    [TestMethod]
    public async Task Shopper_with_no_number_is_not_messaged_but_order_is_placed()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var orderId = await PlaceOrder(shopper);
        Assert.AreEqual(0, _factory.Sms.SendCount);

        var notifications = await GetNotifications(shopper, orderId);
        Assert.AreEqual("NotAttempted", notifications.Single().GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task Dispatch_notifies_and_schedules_followup_then_cancel_calls_it_off()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });
        var orderId = await PlaceOrder(shopper);

        var dispatch = await admin.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatch.StatusCode);

        var afterDispatch = await GetNotifications(shopper, orderId);
        var followUp = afterDispatch.Single(n => n.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.GetProperty("providerStatus").GetString());
        Assert.IsTrue(afterDispatch.Any(n => n.GetProperty("kind").GetString() == "OrderDispatched"));

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);

        var afterCancel = await GetNotifications(shopper, orderId);
        var followUpAfter = afterCancel.Single(n => n.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("Cancelled", followUpAfter.GetProperty("state").GetString());
        Assert.IsTrue(afterCancel.Any(n => n.GetProperty("kind").GetString() == "OrderCancelled"));
    }

    // ---------- Flow 3: operator actions ----------

    [TestMethod]
    public async Task Resend_is_idempotent_per_key()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });
        var orderId = await PlaceOrder(shopper);
        var placed = (await GetNotifications(shopper, orderId)).Single();
        var notificationId = placed.GetProperty("notificationId").GetInt32();

        var sendCountBefore = _factory.Sms.SendCount;

        var first = await ResendAsync(admin, notificationId, "key-1");
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var firstId = (await ReadJson(first)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(sendCountBefore + 1, _factory.Sms.SendCount);

        // Same key: no second message, same notification returned.
        var repeat = await ResendAsync(admin, notificationId, "key-1");
        var repeatId = (await ReadJson(repeat)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(firstId, repeatId);
        Assert.AreEqual(sendCountBefore + 1, _factory.Sms.SendCount);

        // Fresh key: a genuine second attempt.
        var second = await ResendAsync(admin, notificationId, "key-2");
        var secondId = (await ReadJson(second)).GetProperty("notificationId").GetInt32();
        Assert.AreNotEqual(firstId, secondId);
        Assert.AreEqual(sendCountBefore + 2, _factory.Sms.SendCount);
    }

    [TestMethod]
    public async Task Disposing_content_redacts_at_provider_but_keeps_the_record()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });
        var orderId = await PlaceOrder(shopper);
        var placed = (await GetNotifications(shopper, orderId)).Single();
        var notificationId = placed.GetProperty("notificationId").GetInt32();
        var sid = placed.GetProperty("providerMessageSid").GetString();

        var dispose = await admin.DeleteAsync($"api/notifications/{notificationId}/content");
        Assert.AreEqual(HttpStatusCode.OK, dispose.StatusCode);

        // Redacted at the provider ...
        Assert.IsTrue(_factory.Sms.Messages.Single(m => m.Sid == sid).Redacted);

        // ... while the record and its outcome survive.
        var after = (await GetNotifications(shopper, orderId)).Single();
        Assert.IsTrue(after.GetProperty("contentRedacted").GetBoolean());
        Assert.AreEqual(sid, after.GetProperty("providerMessageSid").GetString());
    }

    [TestMethod]
    public async Task Reconciliation_lines_up_provider_and_eshop_records()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = ShopperNumber });
        var orderId = await PlaceOrder(shopper);
        _ = orderId;

        var from = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o");
        var to = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o");

        var report = await ReadJson(await admin.GetAsync($"api/notifications/reconciliation?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"));
        Assert.AreEqual(_factory.Sms.SendingNumber, report.GetProperty("fromNumber").GetString());
        Assert.IsTrue(report.GetProperty("matchedCount").GetInt32() >= 1);
        Assert.AreEqual(0, report.GetProperty("eShopOnlyCount").GetInt32());
    }

    [TestMethod]
    public async Task Operator_endpoints_reject_non_admins()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/orders/1/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/orders/1/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await ResendAsync(shopper, 1, "k")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.DeleteAsync("api/notifications/1/content")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/notifications/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z")).StatusCode);
    }

    // ---------- helpers ----------

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJson(response);
        return json.GetProperty("orderId").GetInt32();
    }

    private static async Task<List<JsonElement>> GetNotifications(HttpClient client, int orderId)
    {
        var json = await ReadJson(await client.GetAsync($"api/orders/{orderId}/notifications"));
        return json.GetProperty("notifications").EnumerateArray().ToList();
    }

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client, int notificationId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/notifications/{notificationId}/resend");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }
}
