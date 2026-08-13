using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Unique per test instance so buyer-scoped data never collides with another test's (the in-memory
    // store is shared across factory instances within the assembly).
    private readonly string _buyerA = "shopper-a-" + Guid.NewGuid().ToString("N");
    private readonly string _buyerB = "shopper-b-" + Guid.NewGuid().ToString("N");

    private static HttpClient ClientFor(NotificationApiFactory factory, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static StringContent JsonBody(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private static async Task<int> RegisterNumberAsync(HttpClient client)
    {
        var response = await client.PostAsync("api/contact-numbers", JsonBody(new { phoneNumber = "416-555-0100" }));
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("contactNumberId").GetInt32();
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var response = await client.PostAsync("api/orders", JsonBody(new { items = new[] { new { catalogItemId = 1, quantity = 2 } } }));
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("orderId").GetInt32();
    }

    private static async Task<JsonElement> GetOrderNotificationsAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("notifications");
    }

    [TestMethod]
    public async Task Register_RequiresAuthentication()
    {
        using var factory = new NotificationApiFactory();
        var client = factory.CreateClient(); // no token

        var response = await client.PostAsync("api/contact-numbers", JsonBody(new { phoneNumber = "416-555-0100" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_RejectsUnusableDestination()
    {
        using var factory = new NotificationApiFactory();
        factory.Sms.LookupValid = false;
        var client = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));

        var response = await client.PostAsync("api/contact-numbers", JsonBody(new { phoneNumber = "nonsense" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_StoresCanonicalForm_AndReturnsId()
    {
        using var factory = new NotificationApiFactory();
        factory.Sms.CanonicalNumber = "+14165550100";
        var client = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));

        var response = await client.PostAsync("api/contact-numbers", JsonBody(new { phoneNumber = "(416) 555 0100" }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.IsTrue(body.GetProperty("contactNumberId").GetInt32() > 0);
        Assert.AreEqual("+14165550100", body.GetProperty("phoneNumber").GetString());
    }

    [TestMethod]
    public async Task ContactNumbers_AreScopedToTheOwner()
    {
        using var factory = new NotificationApiFactory();
        var shopperA = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        var shopperB = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerB));

        var id = await RegisterNumberAsync(shopperA);

        // B cannot see A's number.
        var listB = await ReadJson(await shopperB.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, listB.GetProperty("contactNumbers").GetArrayLength());

        // B cannot delete A's number (indistinguishable from absent).
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopperB.DeleteAsync($"api/contact-numbers/{id}")).StatusCode);

        // A can, and afterwards it is gone.
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopperA.DeleteAsync($"api/contact-numbers/{id}")).StatusCode);
        var listA = await ReadJson(await shopperA.GetAsync("api/contact-numbers"));
        Assert.AreEqual(0, listA.GetProperty("contactNumbers").GetArrayLength());
    }

    [TestMethod]
    public async Task PlaceOrder_TellsTheShopper_AndReturnsOrderId()
    {
        using var factory = new NotificationApiFactory();
        var client = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        await RegisterNumberAsync(client);

        var orderId = await PlaceOrderAsync(client);

        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(1, factory.Sms.SendCount);
        var notifications = await GetOrderNotificationsAsync(client, orderId);
        Assert.AreEqual(1, notifications.GetArrayLength());
        Assert.AreEqual("OrderPlaced", notifications[0].GetProperty("type").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(notifications[0].GetProperty("providerMessageSid").GetString()));
    }

    [TestMethod]
    public async Task PlaceOrder_Succeeds_EvenWhenTheMessageCannotBeSent()
    {
        using var factory = new NotificationApiFactory();
        factory.Sms.SendFault = () => new HttpRequestException("network down");
        var client = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        await RegisterNumberAsync(client);

        var orderId = await PlaceOrderAsync(client); // must still succeed

        Assert.IsTrue(orderId > 0);
        var notifications = await GetOrderNotificationsAsync(client, orderId);
        Assert.AreEqual("submission_failed", notifications[0].GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Dispatch_IsOperatorOnly_AndQueuesAFollowUp()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        var admin = ClientFor(factory, NotificationApiFactory.TokenFor("op", NotificationApiFactory.AdminRole));
        await RegisterNumberAsync(shopper);
        var orderId = await PlaceOrderAsync(shopper);

        // A shopper cannot dispatch.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);

        // The operator can.
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);

        Assert.AreEqual(1, factory.Sms.ScheduleCount);
        var notifications = await GetOrderNotificationsAsync(shopper, orderId);
        var followUp = notifications.EnumerateArray().Single(n => n.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Cancel_CallsOffTheNotYetSentFollowUp()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        var admin = ClientFor(factory, NotificationApiFactory.TokenFor("op", NotificationApiFactory.AdminRole));
        await RegisterNumberAsync(shopper);
        var orderId = await PlaceOrderAsync(shopper);
        await admin.PostAsync($"api/orders/{orderId}/dispatch", null);

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);

        Assert.AreEqual(1, factory.Sms.CancelCount);
        var notifications = await GetOrderNotificationsAsync(shopper, orderId);
        var followUp = notifications.EnumerateArray().Single(n => n.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", followUp.GetProperty("status").GetString());
        Assert.IsTrue(notifications.EnumerateArray().Any(n => n.GetProperty("type").GetString() == "Cancelled"));
    }

    [TestMethod]
    public async Task Resend_IsIdempotentOnKey_AndRequiresAKey()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        var admin = ClientFor(factory, NotificationApiFactory.TokenFor("op", NotificationApiFactory.AdminRole));
        await RegisterNumberAsync(shopper);
        var orderId = await PlaceOrderAsync(shopper);
        var notifications = await GetOrderNotificationsAsync(shopper, orderId);
        var notificationId = notifications[0].GetProperty("notificationId").GetInt32();

        // A key is required.
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.PostAsync($"api/notifications/{notificationId}/resend", null)).StatusCode);

        var sendsBefore = factory.Sms.SendCount;

        var first = await ResendAsync(admin, notificationId, "key-1");
        var repeat = await ResendAsync(admin, notificationId, "key-1");
        var fresh = await ResendAsync(admin, notificationId, "key-2");

        // Same key -> same notification, no second send. Fresh key -> a new send.
        Assert.AreEqual(first, repeat);
        Assert.AreNotEqual(first, fresh);
        Assert.AreEqual(sendsBefore + 2, factory.Sms.SendCount);
    }

    [TestMethod]
    public async Task Reconciliation_IsOperatorOnly()
    {
        using var factory = new NotificationApiFactory();
        var shopper = ClientFor(factory, NotificationApiFactory.TokenFor(_buyerA));
        var admin = ClientFor(factory, NotificationApiFactory.TokenFor("op", NotificationApiFactory.AdminRole));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("o"));
        var url = $"api/notifications/reconciliation?from={from}&to={to}";

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync(url)).StatusCode);

        var adminResponse = await admin.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, adminResponse.StatusCode);
        var report = await ReadJson(adminResponse);
        Assert.AreEqual("+15005550006", report.GetProperty("senderNumber").GetString());
    }

    private static async Task<int> ResendAsync(HttpClient admin, int notificationId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/notifications/{notificationId}/resend");
        request.Headers.Add("Idempotency-Key", key);
        var response = await admin.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("notificationId").GetInt32();
    }
}
