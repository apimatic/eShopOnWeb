using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task DrivesOwnedOrderLifecycleAndOperatorActions()
    {
        FakeSmsProvider.Instance.Reset();
        FakeSmsProvider.Instance.FailNextImmediate = true;
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "+10000000000" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = await IntProperty(contactResponse, "contactNumberId");

        var orderResponse = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Way",
                city = "Test City",
                state = "Test State",
                country = "Canada",
                zipCode = "A1A1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = await IntProperty(orderResponse, "orderId");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).EnsureSuccessStatusCode();

        var notificationsResponse = await shopper.GetAsync($"api/orders/{orderId}/notifications");
        notificationsResponse.EnsureSuccessStatusCode();
        using var notificationsJson = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var notifications = notificationsJson.RootElement.GetProperty("notifications").EnumerateArray().ToArray();
        var failed = notifications.Single(x => x.GetProperty("kind").GetString() == "OrderPlaced");
        Assert.AreEqual("undelivered", failed.GetProperty("providerStatus").GetString());
        var failedNotificationId = failed.GetProperty("notificationId").GetInt32();
        var scheduled = notifications.Single(x => x.GetProperty("kind").GetString() == "DeliveryFollowUp");
        var scheduledProviderId = scheduled.GetProperty("providerMessageId").GetString()!;

        var sendsBeforeResend = FakeSmsProvider.Instance.SendCount;
        var firstResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-request" });
        Assert.AreEqual(HttpStatusCode.Created, firstResend.StatusCode);
        var resentNotificationId = await IntProperty(firstResend, "notificationId");
        var repeatedResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-request" });
        repeatedResend.EnsureSuccessStatusCode();
        Assert.AreEqual(resentNotificationId, await IntProperty(repeatedResend, "notificationId"));
        Assert.AreEqual(sendsBeforeResend + 1, FakeSmsProvider.Instance.SendCount);
        var freshResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "fresh-request" });
        Assert.AreEqual(HttpStatusCode.Created, freshResend.StatusCode);
        Assert.AreNotEqual(resentNotificationId, await IntProperty(freshResend, "notificationId"));
        Assert.AreEqual(sendsBeforeResend + 2, FakeSmsProvider.Instance.SendCount);

        (await admin.PostAsync($"api/orders/{orderId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.AreEqual("canceled", FakeSmsProvider.Instance.Message(scheduledProviderId).Status);

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"api/notifications/{resentNotificationId}/content")).StatusCode);

        var reconciliation = await admin.GetAsync(
            $"api/notifications/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}");
        reconciliation.EnsureSuccessStatusCode();
        StringAssert.Contains(await reconciliation.Content.ReadAsStringAsync(), "matched");
        Assert.AreEqual(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync("api/notifications/reconciliation?from=not-a-date&to=also-not-a-date")).StatusCode);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/contact-numbers/{contactId}")).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync(
                $"api/notifications/{failedNotificationId}/resend",
                new { idempotencyKey = "after-delete" })).StatusCode);

        var otherShopper = Client(ApiTokenHelper.CreateTokenForTest("someone-else@example.com"));
        Assert.AreEqual(HttpStatusCode.NotFound, (await otherShopper.GetAsync($"api/orders/{orderId}/notifications")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await otherShopper.DeleteAsync($"api/contact-numbers/{contactId}")).StatusCode);

        var myOrders = await shopper.GetAsync("api/my-orders");
        myOrders.EnsureSuccessStatusCode();
        StringAssert.Contains(await myOrders.Content.ReadAsStringAsync(), $"\"orderId\":{orderId}");
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> IntProperty(HttpResponseMessage response, string property)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(property).GetInt32();
    }
}
