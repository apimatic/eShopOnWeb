using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SmsNotifications;

[TestClass]
public class SmsNotificationEndpointsTests
{
    private const int SeededCatalogItemId = 1;

    private static StringContent Json(object body) =>
        new(body.ToJson(), Encoding.UTF8, "application/json");

    private static async Task<int> RegisterNumberAsync(SmsNotificationApp app, string token, string number)
    {
        var client = app.ClientFor(token);
        var response = await client.PostAsync("api/contact-numbers", Json(new { phoneNumber = number }));
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadAsStringAsync()).FromJson<RegisterContactNumberResponse>();
        return dto!.ContactNumberId;
    }

    private static async Task<int> PlaceOrderAsync(SmsNotificationApp app, string token)
    {
        var client = app.ClientFor(token);
        var response = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = SeededCatalogItemId, quantity = 1 } } }));
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadAsStringAsync()).FromJson<PlaceOrderResponse>();
        return dto!.OrderId;
    }

    private static async Task<OrderNotificationsResponse> GetNotificationsAsync(SmsNotificationApp app, string token, int orderId)
    {
        var client = app.ClientFor(token);
        var response = await client.GetAsync($"api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync()).FromJson<OrderNotificationsResponse>()!;
    }

    [TestMethod]
    public async Task Shopper_can_register_list_and_delete_own_numbers()
    {
        using var app = new SmsNotificationApp();
        var token = SmsNotificationApp.ShopperToken("shopper-a@test.com");

        var id = await RegisterNumberAsync(app, token, "+1 (555) 000-1234");
        Assert.IsTrue(id > 0);

        var client = app.ClientFor(token);
        var list = (await (await client.GetAsync("api/contact-numbers")).Content.ReadAsStringAsync())
            .FromJson<ListContactNumbersResponse>()!;
        Assert.AreEqual(1, list.ContactNumbers.Count);
        Assert.AreEqual("+15550001234", list.ContactNumbers[0].PhoneNumber); // provider's canonical form stored

        var delete = await client.DeleteAsync($"api/contact-numbers/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var afterList = (await (await client.GetAsync("api/contact-numbers")).Content.ReadAsStringAsync())
            .FromJson<ListContactNumbersResponse>()!;
        Assert.AreEqual(0, afterList.ContactNumbers.Count);
    }

    [TestMethod]
    public async Task A_shopper_cannot_see_or_delete_another_shoppers_number()
    {
        using var app = new SmsNotificationApp();
        var owner = SmsNotificationApp.ShopperToken("owner@test.com");
        var other = SmsNotificationApp.ShopperToken("other@test.com");

        var id = await RegisterNumberAsync(app, owner, "+15550009999");

        var otherClient = app.ClientFor(other);
        var otherList = (await (await otherClient.GetAsync("api/contact-numbers")).Content.ReadAsStringAsync())
            .FromJson<ListContactNumbersResponse>()!;
        Assert.AreEqual(0, otherList.ContactNumbers.Count);

        var deleteAttempt = await otherClient.DeleteAsync($"api/contact-numbers/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteAttempt.StatusCode);
    }

    [TestMethod]
    public async Task Register_rejects_a_number_the_provider_considers_unusable()
    {
        using var app = new SmsNotificationApp();
        var client = app.ClientFor(SmsNotificationApp.ShopperToken("shopper@test.com"));

        var response = await client.PostAsync("api/contact-numbers", Json(new { phoneNumber = "invalid-number" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Placing_an_order_notifies_the_shopper_and_records_the_notification()
    {
        using var app = new SmsNotificationApp();
        var token = SmsNotificationApp.ShopperToken("buyer@test.com");
        await RegisterNumberAsync(app, token, "+15551112222");

        var orderId = await PlaceOrderAsync(app, token);
        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(1, app.Sms.ImmediateSendCount);

        var notifications = await GetNotificationsAsync(app, token, orderId);
        Assert.AreEqual(1, notifications.Notifications.Count);
        var placed = notifications.Notifications.Single();
        Assert.AreEqual("OrderPlaced", placed.Kind);
        Assert.IsTrue(placed.NotificationId > 0);
        Assert.IsFalse(string.IsNullOrEmpty(placed.ProviderMessageSid));
        Assert.AreEqual("delivered", placed.Status); // status refreshed from the provider on read
    }

    [TestMethod]
    public async Task A_shopper_with_no_number_on_file_is_simply_not_messaged()
    {
        using var app = new SmsNotificationApp();
        var token = SmsNotificationApp.ShopperToken("nonumber@test.com");

        var orderId = await PlaceOrderAsync(app, token);
        Assert.AreEqual(0, app.Sms.ImmediateSendCount);

        var notifications = await GetNotificationsAsync(app, token, orderId);
        Assert.AreEqual(0, notifications.Notifications.Count);
    }

    [TestMethod]
    public async Task Order_notifications_are_scoped_to_the_owner()
    {
        using var app = new SmsNotificationApp();
        var owner = SmsNotificationApp.ShopperToken("owner2@test.com");
        var other = SmsNotificationApp.ShopperToken("other2@test.com");

        var orderId = await PlaceOrderAsync(app, owner);

        var otherClient = app.ClientFor(other);
        var response = await otherClient.GetAsync($"api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Operator_actions_require_the_administrator_role()
    {
        using var app = new SmsNotificationApp();
        var shopper = SmsNotificationApp.ShopperToken("plainshopper@test.com");
        var orderId = await PlaceOrderAsync(app, shopper);
        var client = app.ClientFor(shopper);

        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync("api/notifications/1/resend?idempotencyKey=k", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.DeleteAsync("api/notifications/1/content")).StatusCode);
        var recon = await client.GetAsync($"api/notifications/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.Forbidden, recon.StatusCode);
    }

    [TestMethod]
    public async Task Dispatch_notifies_and_schedules_a_follow_up_that_cancel_calls_off()
    {
        using var app = new SmsNotificationApp();
        var shopper = SmsNotificationApp.ShopperToken("dispatchee@test.com");
        var admin = SmsNotificationApp.AdminToken();
        await RegisterNumberAsync(app, shopper, "+15553334444");
        var orderId = await PlaceOrderAsync(app, shopper);

        var adminClient = app.ClientFor(admin);
        var dispatch = await adminClient.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.AreEqual(2, app.Sms.ImmediateSendCount); // placed + on-its-way
        Assert.AreEqual(1, app.Sms.ScheduleCount);      // the follow-up queued with the provider

        var cancel = await adminClient.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.AreEqual(1, app.Sms.CanceledSids.Count); // the scheduled follow-up was called off
        Assert.AreEqual(3, app.Sms.ImmediateSendCount); // + cancellation message

        var notifications = await GetNotificationsAsync(app, shopper, orderId);
        var followUp = notifications.Notifications.Single(n => n.Kind == "DeliveryFollowUp");
        Assert.IsTrue(followUp.IsScheduled);
        Assert.AreEqual("canceled", followUp.Status);
    }

    [TestMethod]
    public async Task Resend_is_idempotent_per_key_but_a_fresh_key_sends_again()
    {
        using var app = new SmsNotificationApp();
        var shopper = SmsNotificationApp.ShopperToken("resendee@test.com");
        var admin = SmsNotificationApp.AdminToken();
        await RegisterNumberAsync(app, shopper, "+15555556666");
        var orderId = await PlaceOrderAsync(app, shopper);
        var placed = (await GetNotificationsAsync(app, shopper, orderId)).Notifications.Single();
        Assert.AreEqual(1, app.Sms.ImmediateSendCount);

        var adminClient = app.ClientFor(admin);

        // First resend under key K1.
        var first = await ResendAsync(adminClient, placed.NotificationId, "K1");
        Assert.IsFalse(first.Replayed);
        Assert.AreEqual(2, app.Sms.ImmediateSendCount);

        // Repeat under the SAME key: no second message, same notification returned.
        var replay = await ResendAsync(adminClient, placed.NotificationId, "K1");
        Assert.IsTrue(replay.Replayed);
        Assert.AreEqual(first.NotificationId, replay.NotificationId);
        Assert.AreEqual(2, app.Sms.ImmediateSendCount);

        // A genuine fresh key sends again.
        var second = await ResendAsync(adminClient, placed.NotificationId, "K2");
        Assert.IsFalse(second.Replayed);
        Assert.AreNotEqual(first.NotificationId, second.NotificationId);
        Assert.AreEqual(3, app.Sms.ImmediateSendCount);
    }

    [TestMethod]
    public async Task Disposing_content_redacts_it_at_the_provider_while_the_record_survives()
    {
        using var app = new SmsNotificationApp();
        var shopper = SmsNotificationApp.ShopperToken("privacy@test.com");
        var admin = SmsNotificationApp.AdminToken();
        await RegisterNumberAsync(app, shopper, "+15557778888");
        var orderId = await PlaceOrderAsync(app, shopper);
        var placed = (await GetNotificationsAsync(app, shopper, orderId)).Notifications.Single();

        var adminClient = app.ClientFor(admin);
        var dispose = await adminClient.DeleteAsync($"api/notifications/{placed.NotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.IsTrue(app.Sms.RedactedSids.Contains(placed.ProviderMessageSid!)); // redacted at the provider

        var after = (await GetNotificationsAsync(app, shopper, orderId)).Notifications.Single();
        Assert.IsTrue(after.ContentDisposed);                        // fact of disposal survives
        Assert.IsFalse(string.IsNullOrEmpty(after.ProviderMessageSid)); // send-record survives
        Assert.IsFalse(string.IsNullOrEmpty(after.Status));           // outcome survives
    }

    [TestMethod]
    public async Task Reconciliation_lines_provider_and_eshop_records_up_and_requires_admin()
    {
        using var app = new SmsNotificationApp();
        var shopper = SmsNotificationApp.ShopperToken("recon@test.com");
        var admin = SmsNotificationApp.AdminToken();
        await RegisterNumberAsync(app, shopper, "+15559990000");
        await PlaceOrderAsync(app, shopper);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var adminClient = app.ClientFor(admin);
        var response = await adminClient.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var report = (await response.Content.ReadAsStringAsync()).FromJson<ReconciliationResponse>()!;
        Assert.AreEqual(app.Sms.SendingNumber, report.FromNumber);
        Assert.IsTrue(report.MatchedCount >= 1);       // the placed-order message is on both sides
        Assert.AreEqual(0, report.ProviderOnlyCount);
        Assert.AreEqual(0, report.EShopOnlyCount);
    }

    private static async Task<ResendNotificationResponse> ResendAsync(HttpClient adminClient, int notificationId, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/notifications/{notificationId}/resend");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await adminClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync()).FromJson<ResendNotificationResponse>()!;
    }
}
