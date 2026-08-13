using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static HttpClient Authed(NotificationApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(object value)
        => new(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

    private static async Task<T> Read<T>(HttpResponseMessage response)
        => JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Json)!;

    private static async Task<int> RegisterNumberAsync(HttpClient client, string number)
    {
        var response = await client.PostAsync("api/contact-numbers", Body(new { number }));
        response.EnsureSuccessStatusCode();
        return (await Read<RegisterContactNumberResponse>(response)).ContactNumberId;
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 1)
    {
        var response = await client.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId, quantity } } }));
        response.EnsureSuccessStatusCode();
        return (await Read<CreateOrderResponse>(response)).OrderId;
    }

    [TestMethod]
    public async Task Register_UsableNumber_Returns201WithId()
    {
        using var factory = new NotificationApiFactory();
        var client = Authed(factory, ApiTokenHelper.GetUserToken("shopper-a@test.com"));

        var response = await client.PostAsync("api/contact-numbers", Body(new { number = "+15145550100" }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue((await Read<RegisterContactNumberResponse>(response)).ContactNumberId > 0);
    }

    [TestMethod]
    public async Task Register_UnusableNumber_Returns400()
    {
        using var factory = new NotificationApiFactory();
        var client = Authed(factory, ApiTokenHelper.GetUserToken("shopper-a@test.com"));

        var response = await client.PostAsync("api/contact-numbers", Body(new { number = "invalid-destination" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ContactNumbers_AreScopedToOwner()
    {
        using var factory = new NotificationApiFactory();
        var shopperA = Authed(factory, ApiTokenHelper.GetUserToken("owner-a@test.com"));
        var shopperB = Authed(factory, ApiTokenHelper.GetUserToken("owner-b@test.com"));

        var idA = await RegisterNumberAsync(shopperA, "+15145550100");

        // B cannot see A's number.
        var bList = await Read<ListContactNumbersResponse>(await shopperB.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, bList.ContactNumbers.Count);

        // B cannot delete A's number.
        var bDelete = await shopperB.DeleteAsync($"api/contact-numbers/{idA}");
        Assert.AreEqual(HttpStatusCode.NotFound, bDelete.StatusCode);

        // A still has it.
        var aList = await Read<ListContactNumbersResponse>(await shopperA.GetAsync("api/contact-numbers"));
        Assert.AreEqual(1, aList.ContactNumbers.Count);
        Assert.AreEqual(idA, aList.ContactNumbers[0].ContactNumberId);
    }

    [TestMethod]
    public async Task DeletedNumber_IsNoLongerListed_AndNotMessaged()
    {
        using var factory = new NotificationApiFactory();
        var client = Authed(factory, ApiTokenHelper.GetUserToken("shopper-del@test.com"));

        var id = await RegisterNumberAsync(client, "+15145550100");
        var delete = await client.DeleteAsync($"api/contact-numbers/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await Read<ListContactNumbersResponse>(await client.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, list.ContactNumbers.Count);

        // With no number on file, placing an order sends nothing.
        await PlaceOrderAsync(client);
        Assert.AreEqual(0, factory.Sms.SendCount);
    }

    [TestMethod]
    public async Task PlaceOrder_SendsPlacedNotification_AndReturnsOrderId()
    {
        using var factory = new NotificationApiFactory();
        var client = Authed(factory, ApiTokenHelper.GetUserToken("shopper-order@test.com"));

        await RegisterNumberAsync(client, "+15145550100");
        var orderId = await PlaceOrderAsync(client);

        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(1, factory.Sms.SendCount);
        Assert.IsTrue(factory.Sms.SentBodies.Single().Contains("placed"));
    }

    [TestMethod]
    public async Task SendFailure_DoesNotFailOrder_AndIsRecorded()
    {
        using var factory = new NotificationApiFactory();
        var client = Authed(factory, ApiTokenHelper.GetUserToken("shopper-fail@test.com"));

        await RegisterNumberAsync(client, "+15145550100");
        factory.Sms.ThrowOnSend = true;

        var response = await client.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId = 1, quantity = 1 } } }));

        // The order still succeeds even though the message could not be sent.
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var orderId = (await Read<CreateOrderResponse>(response)).OrderId;

        var notifications = await Read<OrderNotificationsResponse>(
            await client.GetAsync($"api/orders/{orderId}/notifications"));
        Assert.AreEqual(1, notifications.Notifications.Count);
        Assert.AreEqual("send_failed", notifications.Notifications[0].Status);
    }

    [TestMethod]
    public async Task Dispatch_IsAdminOnly_AndSchedulesFollowUp()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-dispatch@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        var orderId = await PlaceOrderAsync(shopper);

        // A shopper cannot dispatch.
        var forbidden = await shopper.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // An operator can.
        var dispatched = await admin.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatched.StatusCode);

        Assert.AreEqual(1, factory.Sms.ScheduledSids.Count);
        Assert.IsTrue(factory.Sms.SentBodies.Any(b => b.Contains("on its way")));
    }

    [TestMethod]
    public async Task Cancel_CallsOffPendingFollowUp()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-cancel@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        var orderId = await PlaceOrderAsync(shopper);
        await admin.PostAsync($"api/orders/{orderId}/dispatch", null);
        var scheduledSid = factory.Sms.ScheduledSids.Single();

        var cancelled = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode);

        // The follow-up the provider was holding is called off, so it never reaches the shopper.
        Assert.IsTrue(factory.Sms.CanceledSids.Contains(scheduledSid));
        Assert.IsTrue(factory.Sms.SentBodies.Any(b => b.Contains("cancelled")));
    }

    [TestMethod]
    public async Task DispatchTwice_Returns409()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-2x@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        var orderId = await PlaceOrderAsync(shopper);

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
    }

    [TestMethod]
    public async Task OrderNotifications_AreHiddenFromOtherShoppers()
    {
        using var factory = new NotificationApiFactory();
        var owner = Authed(factory, ApiTokenHelper.GetUserToken("owner@test.com"));
        var other = Authed(factory, ApiTokenHelper.GetUserToken("other@test.com"));

        await RegisterNumberAsync(owner, "+15145550100");
        var orderId = await PlaceOrderAsync(owner);

        // The order's owner can see its notifications.
        var ownerView = await owner.GetAsync($"api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.OK, ownerView.StatusCode);

        // Another shopper cannot even learn it exists.
        var otherView = await other.GetAsync($"api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, otherView.StatusCode);
    }

    [TestMethod]
    public async Task Resend_SameKeyDoesNotSendTwice_FreshKeyDoes()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-resend@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        var orderId = await PlaceOrderAsync(shopper);
        var notifications = await Read<OrderNotificationsResponse>(
            await shopper.GetAsync($"api/orders/{orderId}/notifications"));
        var notificationId = notifications.Notifications.Single().NotificationId;

        var sendsBefore = factory.Sms.SendCount;

        var first = await ResendAsync(admin, notificationId, "key-1");
        Assert.AreEqual(sendsBefore + 1, factory.Sms.SendCount);

        // Same key: no second message; the earlier result is returned.
        var repeat = await ResendAsync(admin, notificationId, "key-1");
        Assert.AreEqual(sendsBefore + 1, factory.Sms.SendCount);
        Assert.AreEqual(first, repeat);

        // Fresh key: a genuine second attempt.
        var second = await ResendAsync(admin, notificationId, "key-2");
        Assert.AreEqual(sendsBefore + 2, factory.Sms.SendCount);
        Assert.AreNotEqual(first, second);
    }

    private static async Task<int> ResendAsync(HttpClient admin, int notificationId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/notifications/{notificationId}/resend");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await admin.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await Read<ResendNotificationResponse>(response)).NotificationId;
    }

    [TestMethod]
    public async Task DisposeContent_RedactsAtProvider_AndClearsBody_ButKeepsRecord()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-dispose@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        var orderId = await PlaceOrderAsync(shopper);
        var before = (await Read<OrderNotificationsResponse>(
            await shopper.GetAsync($"api/orders/{orderId}/notifications"))).Notifications.Single();

        var dispose = await admin.DeleteAsync($"api/notifications/{before.NotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.IsTrue(factory.Sms.RedactedSids.Contains(before.ProviderMessageSid!));

        var after = (await Read<OrderNotificationsResponse>(
            await shopper.GetAsync($"api/orders/{orderId}/notifications"))).Notifications.Single();
        Assert.IsTrue(after.ContentDisposed);
        Assert.IsNull(after.Body);
        // The fact it was sent and what became of it survive.
        Assert.AreEqual(before.ProviderMessageSid, after.ProviderMessageSid);
        Assert.IsFalse(string.IsNullOrEmpty(after.Status));
    }

    [TestMethod]
    public async Task Reconciliation_ReportsMessagesOverRange()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-recon@test.com"));
        var admin = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        await RegisterNumberAsync(shopper, "+15145550100");
        await PlaceOrderAsync(shopper);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));

        var response = await admin.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var report = await Read<ReconciliationResponse>(response);
        Assert.IsTrue(report.MatchedCount >= 1);
    }

    [TestMethod]
    public async Task Reconciliation_IsAdminOnly()
    {
        using var factory = new NotificationApiFactory();
        var shopper = Authed(factory, ApiTokenHelper.GetUserToken("shopper-recon2@test.com"));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var response = await shopper.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
