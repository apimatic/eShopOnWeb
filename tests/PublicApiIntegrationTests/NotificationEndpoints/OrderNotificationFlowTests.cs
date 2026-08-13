using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    private const string CanadianNumber = "+16135550142";
    private const int SeededCatalogItemId = 1;

    private static HttpClient ClientFor(NotificationApiFactory factory, string token)
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

    private static async Task<int> RegisterNumber(HttpClient client, string number)
    {
        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = number });
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("contactNumberId").GetInt32();
    }

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = SeededCatalogItemId, quantity = 2 } }
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("orderId").GetInt32();
    }

    private static async Task<(int notificationId, string? sid)> FirstNotification(HttpClient client, int orderId)
    {
        var notifications = await ReadJson(await client.GetAsync($"api/orders/{orderId}/notifications"));
        var first = notifications.GetProperty("notifications").EnumerateArray().First();
        var sid = first.GetProperty("providerMessageSid").ValueKind == JsonValueKind.Null
            ? null
            : first.GetProperty("providerMessageSid").GetString();
        return (first.GetProperty("notificationId").GetInt32(), sid);
    }

    [TestMethod]
    public async Task Register_ReturnsContactNumberId_AndStoresProviderCanonicalForm()
    {
        using var factory = new NotificationApiFactory();
        var client = ClientFor(factory, TestTokens.NewShopper());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "+1 (613) 555-0142" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.IsTrue(body.GetProperty("contactNumberId").GetInt32() > 0);
        // The provider's canonical form is stored, not what the caller typed.
        Assert.AreEqual(CanadianNumber, body.GetProperty("phoneNumber").GetString());
    }

    [TestMethod]
    public async Task Register_RejectsNumberProviderCannotUse()
    {
        using var factory = new NotificationApiFactory();
        factory.Gateway.InvalidNumbers.Add("+15550001111");
        var client = ClientFor(factory, TestTokens.NewShopper());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { phoneNumber = "+15550001111" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ContactNumbers_AreScopedToOwner()
    {
        using var factory = new NotificationApiFactory();
        var owner = ClientFor(factory, TestTokens.NewShopper());
        var other = ClientFor(factory, TestTokens.NewShopper());

        var contactId = await RegisterNumber(owner, CanadianNumber);

        // The other shopper cannot see it...
        var otherList = await ReadJson(await other.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, otherList.GetProperty("contactNumbers").GetArrayLength());

        // ...and cannot delete it.
        var otherDelete = await other.DeleteAsync($"api/contact-numbers/{contactId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);

        // The owner can delete it, after which it no longer appears.
        var ownerDelete = await owner.DeleteAsync($"api/contact-numbers/{contactId}");
        Assert.AreEqual(HttpStatusCode.OK, ownerDelete.StatusCode);
        var ownerList = await ReadJson(await owner.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, ownerList.GetProperty("contactNumbers").GetArrayLength());
    }

    [TestMethod]
    public async Task PlaceOrder_ReturnsOrderId_AndMessagesTheShopper()
    {
        using var factory = new NotificationApiFactory();
        var client = ClientFor(factory, TestTokens.NewShopper());
        await RegisterNumber(client, CanadianNumber);

        var orderId = await PlaceOrder(client);

        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(1, factory.Gateway.Sends.Count, "the shopper should be told their order was placed");
    }

    [TestMethod]
    public async Task PlaceOrder_WithNoNumberOnFile_IsNotMessaged()
    {
        using var factory = new NotificationApiFactory();
        var client = ClientFor(factory, TestTokens.NewShopper());

        var orderId = await PlaceOrder(client);

        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(0, factory.Gateway.Sends.Count);
    }

    [TestMethod]
    public async Task Dispatch_And_Cancel_AreOperatorOnly()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, TestTokens.NewShopper());
        await RegisterNumber(shopper, CanadianNumber);
        var orderId = await PlaceOrder(shopper);

        var shopperDispatch = await shopper.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperDispatch.StatusCode);

        var shopperCancel = await shopper.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperCancel.StatusCode);
    }

    [TestMethod]
    public async Task Dispatch_QueuesFollowUp_And_Cancel_CallsItOff()
    {
        using var factory = new NotificationApiFactory();
        var shopperToken = TestTokens.NewShopper();
        var shopper = ClientFor(factory, shopperToken);
        var admin = ClientFor(factory, TestTokens.NewAdmin());
        await RegisterNumber(shopper, CanadianNumber);
        var orderId = await PlaceOrder(shopper);

        var dispatch = await admin.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.AreEqual(1, factory.Gateway.Schedules.Count, "a delivery follow-up should be queued with the provider");
        Assert.AreEqual(0, factory.Gateway.Canceled.Count);

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.AreEqual(1, factory.Gateway.Canceled.Count, "the queued follow-up must be called off so it never reaches the customer");

        // The follow-up notification reflects that it was canceled.
        var notifications = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var followUp = notifications.GetProperty("notifications").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", followUp.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Resend_IsIdempotentPerKey()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, TestTokens.NewShopper());
        var admin = ClientFor(factory, TestTokens.NewAdmin());
        await RegisterNumber(shopper, CanadianNumber);
        var orderId = await PlaceOrder(shopper);

        var (notificationId, _) = await FirstNotification(shopper, orderId);
        var sendsAfterPlace = factory.Gateway.Sends.Count;

        // Same key twice -> one extra send, same notificationId returned.
        var first = await admin.PostAsJsonAsync($"api/notifications/{notificationId}/resend", new { idempotencyKey = "key-1" });
        var second = await admin.PostAsJsonAsync($"api/notifications/{notificationId}/resend", new { idempotencyKey = "key-1" });
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var firstId = (await ReadJson(first)).GetProperty("notificationId").GetInt32();
        var secondId = (await ReadJson(second)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(firstId, secondId);
        Assert.AreEqual(sendsAfterPlace + 1, factory.Gateway.Sends.Count, "a repeat under the same key must not send again");

        // A fresh key is a genuine second attempt.
        var third = await admin.PostAsJsonAsync($"api/notifications/{notificationId}/resend", new { idempotencyKey = "key-2" });
        Assert.AreEqual(HttpStatusCode.OK, third.StatusCode);
        Assert.AreEqual(sendsAfterPlace + 2, factory.Gateway.Sends.Count);
    }

    [TestMethod]
    public async Task DisposeContent_RedactsAtProvider_AndBlocksResend()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, TestTokens.NewShopper());
        var admin = ClientFor(factory, TestTokens.NewAdmin());
        await RegisterNumber(shopper, CanadianNumber);
        var orderId = await PlaceOrder(shopper);

        var (notificationId, _) = await FirstNotification(shopper, orderId);

        var dispose = await admin.DeleteAsync($"api/notifications/{notificationId}/content");
        Assert.AreEqual(HttpStatusCode.OK, dispose.StatusCode);
        Assert.AreEqual(1, factory.Gateway.Redacted.Count, "the content must be disposed at the provider, not merely hidden");

        // The record survives and shows the content is redacted.
        var after = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var entry = after.GetProperty("notifications").EnumerateArray().Single(n => n.GetProperty("notificationId").GetInt32() == notificationId);
        Assert.IsTrue(entry.GetProperty("contentRedacted").GetBoolean());

        // A disposed message cannot be resent.
        var resend = await admin.PostAsJsonAsync($"api/notifications/{notificationId}/resend", new { idempotencyKey = "after-dispose" });
        Assert.AreEqual(HttpStatusCode.Conflict, resend.StatusCode);
    }

    [TestMethod]
    public async Task SendFailure_DoesNotFailTheOrderOperation()
    {
        using var factory = new NotificationApiFactory();
        factory.Gateway.FailSends = true;
        var shopper = ClientFor(factory, TestTokens.NewShopper());
        await RegisterNumber(shopper, CanadianNumber);

        // The order is still placed even though the message cannot be sent.
        var orderId = await PlaceOrder(shopper);
        Assert.IsTrue(orderId > 0);

        var (_, sid) = await FirstNotification(shopper, orderId);
        Assert.IsNull(sid, "a failed send has no provider message id");
        var notifications = await ReadJson(await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        Assert.AreEqual("send_failed", notifications.GetProperty("notifications").EnumerateArray().First().GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Reconciliation_IsOperatorOnly_AndLinesUpProviderAgainstEShop()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, TestTokens.NewShopper());
        var admin = ClientFor(factory, TestTokens.NewAdmin());
        await RegisterNumber(shopper, CanadianNumber);
        var orderId = await PlaceOrder(shopper);

        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("o");
        var to = DateTimeOffset.UtcNow.AddHours(1).ToString("o");

        // A shopper cannot run reconciliation.
        var forbidden = await shopper.GetAsync($"api/notifications/reconciliation?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Seed the provider side: the SID eShop recorded, plus one the provider knows and eShop does not.
        var (_, sid) = await FirstNotification(shopper, orderId);
        factory.Gateway.ProviderMessages.Add(new ProviderMessage { Sid = sid, Status = "delivered", From = factory.Gateway.SenderNumber, DateSent = "now" });
        factory.Gateway.ProviderMessages.Add(new ProviderMessage { Sid = "SM_provider_only", Status = "delivered", From = factory.Gateway.SenderNumber, DateSent = "now" });

        var report = await ReadJson(await admin.GetAsync($"api/notifications/reconciliation?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"));
        var r = report.GetProperty("report");
        Assert.AreEqual(1, r.GetProperty("matched").GetArrayLength());
        Assert.AreEqual(1, r.GetProperty("providerOnly").GetArrayLength());
        Assert.AreEqual(factory.Gateway.SenderNumber, r.GetProperty("fromNumber").GetString());
    }
}
