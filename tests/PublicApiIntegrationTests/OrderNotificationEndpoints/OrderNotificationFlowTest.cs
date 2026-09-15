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
    public async Task DrivesShopperAndOperatorFlowsWithOwnershipAndIdempotency()
    {
        var reachable = Environment.GetEnvironmentVariable("TWILIO_TEST_TO_NUMBER")!;
        var unreachable = Environment.GetEnvironmentVariable("TWILIO_UNREACHABLE_TO_NUMBER")!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(reachable));
        Assert.IsFalse(string.IsNullOrWhiteSpace(unreachable));

        using var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        using var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var unauthenticated = await ProgramTest.NewClient.GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var reachableContact = await CreateContactAsync(shopper, reachable);
        var unreachableContact = await CreateContactAsync(shopper, unreachable);
        Assert.AreNotEqual(reachableContact, unreachableContact);

        var orderId = await CreateOrderAsync(shopper);
        var forbidden = await shopper.PostAsync($"api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).EnsureSuccessStatusCode();

        var notifications = await GetNotificationsAsync(shopper, orderId);
        Assert.IsTrue(notifications.EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "OrderPlaced"));
        Assert.AreEqual(2, notifications.EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "DeliveryFollowUp" && x.GetProperty("status").GetString() == "scheduled"));

        var failed = notifications.EnumerateArray().First(x =>
            x.GetProperty("kind").GetString() == "OrderPlaced" &&
            x.GetProperty("status").GetString() == "undelivered");
        var failedId = failed.GetProperty("notificationId").GetInt32();

        var firstResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedId}/resend",
            new { idempotencyKey = "same-attempt" });
        firstResend.EnsureSuccessStatusCode();
        var firstResendId = (await JsonDocument.ParseAsync(await firstResend.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("notificationId").GetInt32();
        var secondResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedId}/resend",
            new { idempotencyKey = "same-attempt" });
        secondResend.EnsureSuccessStatusCode();
        var secondResendId = (await JsonDocument.ParseAsync(await secondResend.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("notificationId").GetInt32();
        Assert.AreEqual(firstResendId, secondResendId);

        var freshResend = await admin.PostAsJsonAsync(
            $"api/notifications/{failedId}/resend",
            new { idempotencyKey = "fresh-attempt" });
        freshResend.EnsureSuccessStatusCode();
        var freshResendId = (await JsonDocument.ParseAsync(await freshResend.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("notificationId").GetInt32();
        Assert.AreNotEqual(firstResendId, freshResendId);

        var refreshed = await GetNotificationsAsync(shopper, orderId);
        Assert.AreEqual(2, refreshed.EnumerateArray().Count(x =>
            x.GetProperty("resendOfNotificationId").ValueKind == JsonValueKind.Number &&
            x.GetProperty("resendOfNotificationId").GetInt32() == failedId));

        var providerSid = failed.GetProperty("providerMessageSid").GetString()!;
        var dispose = await admin.DeleteAsync($"api/notifications/{failedId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.IsTrue(ProgramTest.MessagingProvider.IsContentDisposed(providerSid));
        var disposed = (await GetNotificationsAsync(shopper, orderId)).EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == failedId);
        Assert.AreEqual(JsonValueKind.Null, disposed.GetProperty("content").ValueKind);

        (await admin.PostAsync($"api/orders/{orderId}/cancel", null)).EnsureSuccessStatusCode();
        var cancelled = await GetNotificationsAsync(shopper, orderId);
        Assert.AreEqual(2, cancelled.EnumerateArray().Count(x =>
            x.GetProperty("kind").GetString() == "DeliveryFollowUp" &&
            x.GetProperty("status").GetString() == "canceled"));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));
        var reconciliation = await admin.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        var report = await JsonDocument.ParseAsync(await reconciliation.Content.ReadAsStreamAsync());
        Assert.IsTrue(report.RootElement.GetProperty("entries").GetArrayLength() > 0);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/contact-numbers/{reachableContact}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/contact-numbers/{unreachableContact}")).StatusCode);
        var contacts = await shopper.GetFromJsonAsync<JsonElement>("api/contact-numbers");
        Assert.AreEqual(0, contacts.GetArrayLength());
        var orderWithoutContacts = await CreateOrderAsync(shopper);
        Assert.AreEqual(0, (await GetNotificationsAsync(shopper, orderWithoutContacts)).GetArrayLength());
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> CreateContactAsync(HttpClient client, string number)
    {
        var response = await client.PostAsJsonAsync("api/contact-numbers", new { mobileNumber = number });
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("contactNumberId").GetInt32();
    }

    private static async Task<int> CreateOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new
            {
                street = "Test Street",
                city = "Toronto",
                state = "ON",
                country = "Canada",
                zipCode = "A1A 1A1"
            }
        });
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("orderId").GetInt32();
    }

    private static async Task<JsonElement> GetNotificationsAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.Clone();
    }
}
