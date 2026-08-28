using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationEndpointTests
{
    [TestMethod]
    public async Task InvalidNumberIsRejectedAndSendFailureDoesNotFailOrder()
    {
        await using var factory = new NotificationApiFactory();
        using var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());

        factory.Twilio.NumberIsValid = false;
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await shopper.PostAsJsonAsync("/api/contact-numbers", new { mobileNumber = "invalid" })).StatusCode);
        factory.Twilio.NumberIsValid = true;
        Assert.AreEqual(HttpStatusCode.Created,
            (await shopper.PostAsJsonAsync("/api/contact-numbers", new { mobileNumber = "5555550123" })).StatusCode);

        factory.Twilio.ThrowOnSend = true;
        var orderResponse = await PlaceOrder(shopper);
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = await IntProperty(orderResponse, "orderId");
        var notifications = await shopper.GetFromJsonAsync<JsonElement[]>($"/api/orders/{orderId}/notifications");
        Assert.AreEqual("send_failed", notifications!.Single().GetProperty("providerStatus").GetString());
    }

    [TestMethod]
    public async Task ShopperOwnershipAndContactDeletionAreEnforced()
    {
        await using var factory = new NotificationApiFactory();
        using var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        using var otherShopper = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var created = await shopper.PostAsJsonAsync("/api/contact-numbers", new { mobileNumber = "(555) 555-0123" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var contactId = await IntProperty(created, "contactNumberId");

        var otherList = await otherShopper.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(0, otherList!.Length);
        Assert.AreEqual(HttpStatusCode.NotFound, (await otherShopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        var ownList = await shopper.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(0, ownList!.Length);

        var order = await PlaceOrder(shopper);
        Assert.AreEqual(HttpStatusCode.Created, order.StatusCode);
        Assert.AreEqual(0, factory.Twilio.Sends.Count, "A deleted number must never be messaged again.");
    }

    [TestMethod]
    public async Task CompleteOrderFlowSchedulesCancelsResendsRedactsAndReconciles()
    {
        await using var factory = new NotificationApiFactory();
        using var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        using var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        Assert.AreEqual(HttpStatusCode.Created,
            (await shopper.PostAsJsonAsync("/api/contact-numbers", new { mobileNumber = "5555550123" })).StatusCode);

        factory.Twilio.SendStatuses.Enqueue("delivered");
        factory.Twilio.SendStatuses.Enqueue("delivered");
        factory.Twilio.SendStatuses.Enqueue("scheduled");
        factory.Twilio.SendStatuses.Enqueue("delivered");
        factory.Twilio.SendStatuses.Enqueue("undelivered");
        factory.Twilio.SendStatuses.Enqueue("delivered");

        var firstOrderResponse = await PlaceOrder(shopper);
        Assert.AreEqual(HttpStatusCode.Created, firstOrderResponse.StatusCode);
        var firstOrderId = await IntProperty(firstOrderResponse, "orderId");

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/orders/{firstOrderId}/notifications")).StatusCode);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{firstOrderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{firstOrderId}/dispatch", null)).StatusCode);
        Assert.IsTrue(factory.Twilio.Sends.Single(x => x.SendAt is not null).SendAt > DateTimeOffset.UtcNow.AddDays(2));

        factory.Twilio.ThrowOnNextCancellation = true;
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{firstOrderId}/cancel", null)).StatusCode);
        var pendingNotifications = await shopper.GetFromJsonAsync<JsonElement[]>($"/api/orders/{firstOrderId}/notifications");
        Assert.IsTrue(pendingNotifications!.Any(x => x.GetProperty("providerStatus").GetString() == "cancel_pending"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrderNotificationService>()
                .RetryPendingCancellationsAsync(default);
        }
        Assert.AreEqual(2, factory.Twilio.Cancellations.Count);

        var notifications = await shopper.GetFromJsonAsync<JsonElement[]>($"/api/orders/{firstOrderId}/notifications");
        Assert.IsTrue(notifications!.Any(x => x.GetProperty("providerStatus").GetString() == "canceled"));
        var contentNotificationId = notifications!.First(x => x.GetProperty("type").GetString() == "OrderPlaced")
            .GetProperty("notificationId").GetInt32();
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{contentNotificationId}/content")).StatusCode);
        Assert.AreEqual(1, factory.Twilio.Redactions.Count);
        notifications = await shopper.GetFromJsonAsync<JsonElement[]>($"/api/orders/{firstOrderId}/notifications");
        Assert.AreEqual(JsonValueKind.Null,
            notifications!.First(x => x.GetProperty("notificationId").GetInt32() == contentNotificationId)
                .GetProperty("content").ValueKind);

        var failedOrderResponse = await PlaceOrder(shopper);
        var failedOrderId = await IntProperty(failedOrderResponse, "orderId");
        var failedNotifications = await shopper.GetFromJsonAsync<JsonElement[]>($"/api/orders/{failedOrderId}/notifications");
        var failedId = failedNotifications!.Single().GetProperty("notificationId").GetInt32();

        var firstResend = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-1" });
        Assert.AreEqual(HttpStatusCode.Created, firstResend.StatusCode);
        var resendId = await IntProperty(firstResend, "notificationId");
        var sendCount = factory.Twilio.Sends.Count;
        var replay = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-1" });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(resendId, await IntProperty(replay, "notificationId"));
        Assert.AreEqual(sendCount, factory.Twilio.Sends.Count, "An idempotent replay must not send again.");
        var freshAttempt = await admin.PostAsJsonAsync(
            $"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-2" });
        Assert.AreEqual(HttpStatusCode.Created, freshAttempt.StatusCode);
        Assert.AreNotEqual(resendId, await IntProperty(freshAttempt, "notificationId"));
        Assert.AreEqual(sendCount + 1, factory.Twilio.Sends.Count, "A fresh key is a legitimate second attempt.");

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}")).StatusCode);
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        var report = await reconciliation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(report.GetProperty("matchedCount").GetInt32() > 0);

        var myOrders = await shopper.GetFromJsonAsync<JsonElement[]>("/api/my-orders");
        Assert.AreEqual(2, myOrders!.Length);
        Assert.IsTrue(myOrders.Any(x => x.GetProperty("notifications").EnumerateArray()
            .Any(n => n.GetProperty("notificationId").GetInt32() == resendId)));
    }

    private static HttpClient Client(NotificationApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PlaceOrder(HttpClient client) => client.PostAsJsonAsync("/api/orders", new
    {
        items = new[] { new { catalogItemId = 1, quantity = 2 } },
        shippingAddress = new
        {
            street = "1 Test Street",
            city = "Test City",
            state = "ON",
            country = "Canada",
            zipCode = "A1A 1A1"
        }
    });

    private static async Task<int> IntProperty(HttpResponseMessage response, string name)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty(name).GetInt32();
    }
}
