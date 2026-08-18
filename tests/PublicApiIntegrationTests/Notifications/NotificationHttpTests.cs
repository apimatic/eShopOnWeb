using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class NotificationHttpTests
{
    private static HttpClient Client(SmsApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [TestMethod]
    public async Task RegisterContactNumber_ReturnsCreatedWithTopLevelContactNumberId()
    {
        using var factory = new SmsApiFactory();
        factory.Gateway.ValidationCanonical = "+15145550123";
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "514 555 0123" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.IsTrue(body.GetProperty("contactNumberId").GetInt32() > 0);
        Assert.AreEqual("+15145550123", body.GetProperty("phoneNumber").GetString());
    }

    [TestMethod]
    public async Task RegisterContactNumber_RejectsUnusableNumber()
    {
        using var factory = new SmsApiFactory();
        factory.Gateway.ValidationUsable = false;
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "garbage" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task OperatorEndpoints_AreForbiddenForNormalUser()
    {
        using var factory = new SmsApiFactory();
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var dispatch = await client.PostAsync("api/orders/1/dispatch", null);
        var reconcile = await client.GetAsync("api/notifications/reconciliation?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z");
        var resend = await client.PostAsJsonAsync("api/notifications/1/resend", new { idempotencyKey = "k" });

        Assert.AreEqual(HttpStatusCode.Forbidden, dispatch.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconcile.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, resend.StatusCode);
    }

    [TestMethod]
    public async Task FullLifecycle_PlaceDispatchCancel_TracksAndCallsOffTheFollowUp()
    {
        using var factory = new SmsApiFactory();
        factory.Gateway.ValidationCanonical = "+15145550123";
        factory.Gateway.SendStatus = DeliveryStatuses.Delivered; // so refresh settles to a terminal state
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        // Register a number, then place an order.
        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "514 555 0123" });
        var placeResp = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, placeResp.StatusCode);
        var orderId = (await ReadJson(placeResp)).GetProperty("orderId").GetInt32();

        // Dispatch (admin) → schedules a follow-up with the provider.
        var dispatchResp = await admin.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatchResp.StatusCode);
        Assert.AreEqual(1, factory.Gateway.Scheduled.Count);

        // The shopper can see a scheduled follow-up among the notifications.
        var beforeCancel = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var followUp = beforeCancel.GetProperty("notifications").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == NotificationType.DeliveryFollowUp.ToString());
        Assert.AreEqual(DeliveryStatuses.Scheduled, followUp.GetProperty("deliveryStatus").GetString());
        var followUpSid = followUp.GetProperty("providerMessageSid").GetString();

        // Cancel (admin) → the scheduled follow-up is called off before it sends.
        var cancelResp = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelResp.StatusCode);
        CollectionAssert.Contains(factory.Gateway.Canceled, followUpSid);

        var afterCancel = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var followUpAfter = afterCancel.GetProperty("notifications").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == NotificationType.DeliveryFollowUp.ToString());
        Assert.AreEqual(DeliveryStatuses.Canceled, followUpAfter.GetProperty("deliveryStatus").GetString());
    }

    [TestMethod]
    public async Task Resend_UnderSameIdempotencyKey_DoesNotSendTwice()
    {
        using var factory = new SmsApiFactory();
        factory.Gateway.ValidationCanonical = "+15145550123";
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "514 555 0123" });
        var placeResp = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = (await ReadJson(placeResp)).GetProperty("orderId").GetInt32();
        var notifs = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var sourceId = notifs.GetProperty("notifications").EnumerateArray().First().GetProperty("notificationId").GetInt32();
        var sentAfterPlace = factory.Gateway.Sent.Count;

        var first = await ReadJson(await admin.PostAsJsonAsync($"api/notifications/{sourceId}/resend", new { idempotencyKey = "same-key" }));
        var second = await ReadJson(await admin.PostAsJsonAsync($"api/notifications/{sourceId}/resend", new { idempotencyKey = "same-key" }));

        Assert.AreEqual(first.GetProperty("notificationId").GetInt32(), second.GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sentAfterPlace + 1, factory.Gateway.Sent.Count, "The repeated key must not send a second message.");
    }

    [TestMethod]
    public async Task OrderNotifications_OfAnotherShopper_AreNotFound()
    {
        using var factory = new SmsApiFactory();
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());

        // Admin places an order (owned by the admin user).
        var placeResp = await admin.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var adminOrderId = (await ReadJson(placeResp)).GetProperty("orderId").GetInt32();

        // A different shopper cannot see it.
        var response = await shopper.GetAsync($"api/orders/{adminOrderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
