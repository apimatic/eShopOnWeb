using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class OrderNotificationEndpointTest
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static FakeSmsNotificationClient _sms = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _sms = new FakeSmsNotificationClient();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISmsNotificationClient>(_sms);
            });
        });
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task ContactNumberEndpointsAreScopedToTheCaller()
    {
        var shopper = CreateClient(ApiTokenHelper.GetNormalUserToken());
        var other = CreateClient(ApiTokenHelper.GetUserToken("other@microsoft.com"));

        var created = await PostJson<CreateContactNumberResponse>(shopper, "api/contact-numbers", new { phoneNumber = "+15555550100" });
        Assert.IsTrue(created.ContactNumberId > 0);
        Assert.AreEqual("+15555550100", created.CanonicalNumber);

        var listed = await GetJson<ListContactNumbersResponse>(shopper, "api/contact-numbers");
        Assert.AreEqual(1, listed.ContactNumbers.Count);

        var otherList = await GetJson<ListContactNumbersResponse>(other, "api/contact-numbers");
        Assert.AreEqual(0, otherList.ContactNumbers.Count);

        var deleteOther = await other.DeleteAsync($"api/contact-numbers/{created.ContactNumberId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteOther.StatusCode);

        var deleteOwn = await shopper.DeleteAsync($"api/contact-numbers/{created.ContactNumberId}");
        deleteOwn.EnsureSuccessStatusCode();

        var afterDelete = await GetJson<ListContactNumbersResponse>(shopper, "api/contact-numbers");
        Assert.AreEqual(0, afterDelete.ContactNumbers.Count);
    }

    [TestMethod]
    public async Task RejectsANumberTheProviderDoesNotConsiderUsable()
    {
        _sms.RejectLookups = true;
        try
        {
            var shopper = CreateClient(ApiTokenHelper.GetNormalUserToken());
            var response = await shopper.PostAsync("api/contact-numbers", JsonBody(new { phoneNumber = "+15555550199" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            _sms.RejectLookups = false;
        }
    }

    [TestMethod]
    public async Task PlacesDispatchesCancelsAndResendsWithoutFailingTheOrder()
    {
        var shopper = CreateClient(ApiTokenHelper.GetNormalUserToken());
        var admin = CreateClient(ApiTokenHelper.GetAdminUserToken());

        await PostJson<CreateContactNumberResponse>(shopper, "api/contact-numbers", new { phoneNumber = "+15555550123" });

        var placed = await PostJson<CreateOrderResponse>(shopper, "api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        });
        Assert.IsTrue(placed.OrderId > 0);

        var dispatched = await PostJson<DispatchOrderResponse>(admin, $"api/orders/{placed.OrderId}/dispatch", new { });
        Assert.AreEqual("Dispatched", dispatched.Status);

        var notifications = await GetJson<ListOrderNotificationsResponse>(shopper, $"api/orders/{placed.OrderId}/notifications");
        Assert.IsTrue(notifications.Notifications.Any(n => n.Kind == "OrderPlaced"));
        Assert.IsTrue(notifications.Notifications.Any(n => n.Kind == "OrderDispatched"));
        var followUp = notifications.Notifications.Single(n => n.Kind == "DispatchFollowUp");
        Assert.AreEqual("scheduled", followUp.Status);

        var cancelledOrder = await PostJson<CreateOrderResponse>(shopper, "api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        });
        await PostJson<DispatchOrderResponse>(admin, $"api/orders/{cancelledOrder.OrderId}/dispatch", new { });
        var cancel = await PostJson<CancelOrderResponse>(admin, $"api/orders/{cancelledOrder.OrderId}/cancel", new { });
        Assert.AreEqual("Cancelled", cancel.Status);

        var cancelledNotes = await GetJson<ListOrderNotificationsResponse>(admin, $"api/orders/{cancelledOrder.OrderId}/notifications");
        var cancelledFollowUp = cancelledNotes.Notifications.Single(n => n.Kind == "DispatchFollowUp");
        Assert.AreEqual("canceled", cancelledFollowUp.Status);

        var placedNote = notifications.Notifications.First(n => n.Kind == "OrderPlaced");
        var resend = await PostJson<ResendNotificationResponse>(admin, $"api/notifications/{placedNote.NotificationId}/resend", new { idempotencyKey = "key-1" });
        Assert.IsTrue(resend.NotificationId > 0);
        var resendAgain = await PostJson<ResendNotificationResponse>(admin, $"api/notifications/{placedNote.NotificationId}/resend", new { idempotencyKey = "key-1" });
        Assert.AreEqual(resend.NotificationId, resendAgain.NotificationId);

        var redact = await admin.DeleteAsync($"api/notifications/{placedNote.NotificationId}/content");
        redact.EnsureSuccessStatusCode();

        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("o");
        var to = DateTimeOffset.UtcNow.AddHours(1).ToString("o");
        var report = await GetJson<ReconcileNotificationsResponse>(admin, $"api/notifications/reconciliation?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        Assert.IsTrue(report.Matched.Count + report.ApplicationOnly.Count + report.ProviderOnly.Count > 0);

        var forbidden = await shopper.PostAsync($"api/orders/{placed.OrderId}/dispatch", JsonBody(new { }));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent JsonBody(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<T> PostJson<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsync(url, JsonBody(body));
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{url} failed: {(int)response.StatusCode} {text}");
        return text.FromJson<T>()!;
    }

    private static async Task<T> GetJson<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{url} failed: {(int)response.StatusCode} {text}");
        return text.FromJson<T>()!;
    }
}
