using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task ProviderRejectionDoesNotFailOrderCreation()
    {
        var shopper = Client(ApiTokenHelper.GetUserToken("provider-rejection-shopper@example.com"));
        (await shopper.PostAsJsonAsync("api/contact-numbers", new
        {
            phoneNumber = "fake-provider-rejection-destination"
        })).EnsureSuccessStatusCode();

        ProgramTest.SmsProvider.RejectSends = true;
        try
        {
            var response = await shopper.PostAsJsonAsync("api/orders", new
            {
                items = new[] { new { catalogItemId = 1, quantity = 1 } },
                shippingAddress = new
                {
                    street = "1 Failure Test",
                    city = "Test City",
                    state = "TS",
                    country = "Test Country",
                    zipCode = "00000"
                }
            });

            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
            var orderId = await ReadIntAsync(response, "orderId");
            var notifications = await shopper.GetFromJsonAsync<JsonElement[]>(
                $"api/orders/{orderId}/notifications");
            Assert.AreEqual("provider-rejected", notifications!.Single().GetProperty("status").GetString());
        }
        finally
        {
            ProgramTest.SmsProvider.RejectSends = false;
        }
    }

    [TestMethod]
    public async Task DrivesOwnedShopperAndAdministratorFlowsEndToEnd()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var otherShopper = Client(ApiTokenHelper.GetUserToken("another-shopper@example.com"));
        var administrator = Client(ApiTokenHelper.GetAdminUserToken());

        var unauthorized = await ProgramTest.NewClient.GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var register = await shopper.PostAsJsonAsync("api/contact-numbers", new
        {
            phoneNumber = "fake-unreachable-destination"
        });
        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode);
        var contactNumberId = await ReadIntAsync(register, "contactNumberId");

        var otherList = await otherShopper.GetFromJsonAsync<JsonElement[]>("api/contact-numbers");
        Assert.AreEqual(0, otherList!.Length);
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            (await otherShopper.DeleteAsync($"api/contact-numbers/{contactNumberId}")).StatusCode);

        var createOrder = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Street",
                city = "Toronto",
                state = "ON",
                country = "Canada",
                zipCode = "A1A 1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, createOrder.StatusCode);
        var orderId = await ReadIntAsync(createOrder, "orderId");

        Assert.AreEqual(
            HttpStatusCode.NotFound,
            (await otherShopper.GetAsync($"api/orders/{orderId}/notifications")).StatusCode);

        var placedNotifications = await shopper.GetFromJsonAsync<JsonElement[]>(
            $"api/orders/{orderId}/notifications");
        Assert.AreEqual(1, placedNotifications!.Length);
        Assert.AreEqual("undelivered", placedNotifications[0].GetProperty("status").GetString());
        var placedNotificationId = placedNotifications[0].GetProperty("notificationId").GetInt32();

        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        (await administrator.PostAsync($"api/orders/{orderId}/dispatch", null)).EnsureSuccessStatusCode();
        (await administrator.PostAsync($"api/orders/{orderId}/cancel", null)).EnsureSuccessStatusCode();

        var afterCancel = await shopper.GetFromJsonAsync<JsonElement[]>(
            $"api/orders/{orderId}/notifications");
        Assert.IsTrue(afterCancel!.Any(x =>
            x.GetProperty("kind").GetString() == "DeliveryFollowUp" &&
            x.GetProperty("status").GetString() == "canceled"));

        var sendsBeforeResend = ProgramTest.SmsProvider.SendCount;
        var firstResend = await administrator.PostAsJsonAsync(
            $"api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "order-flow-resend-1" });
        Assert.AreEqual(HttpStatusCode.Created, firstResend.StatusCode);
        var resentNotificationId = await ReadIntAsync(firstResend, "notificationId");

        var duplicateResend = await administrator.PostAsJsonAsync(
            $"api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "order-flow-resend-1" });
        Assert.AreEqual(HttpStatusCode.Created, duplicateResend.StatusCode);
        Assert.AreEqual(resentNotificationId, await ReadIntAsync(duplicateResend, "notificationId"));
        Assert.AreEqual(sendsBeforeResend + 1, ProgramTest.SmsProvider.SendCount);

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            (await administrator.DeleteAsync($"api/notifications/{placedNotificationId}/content")).StatusCode);
        var afterDisposal = await shopper.GetFromJsonAsync<JsonElement[]>(
            $"api/orders/{orderId}/notifications");
        var disposed = afterDisposal!.Single(x => x.GetProperty("notificationId").GetInt32() == placedNotificationId);
        Assert.AreEqual(JsonValueKind.Null, disposed.GetProperty("content").ValueKind);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await administrator.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        var report = await reconciliation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(report.GetProperty("entries").GetArrayLength() > 0);
        Assert.IsTrue(report.GetProperty("counts").GetProperty("matched").GetInt32() > 0);

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"api/contact-numbers/{contactNumberId}")).StatusCode);
        var sendCountAfterDelete = ProgramTest.SmsProvider.SendCount;
        var resendAfterDelete = await administrator.PostAsJsonAsync(
            $"api/notifications/{resentNotificationId}/resend",
            new { idempotencyKey = "order-flow-resend-after-delete" });
        Assert.AreEqual(HttpStatusCode.Conflict, resendAfterDelete.StatusCode);
        Assert.AreEqual(sendCountAfterDelete, ProgramTest.SmsProvider.SendCount);
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> ReadIntAsync(HttpResponseMessage response, string property)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty(property).GetInt32();
    }
}
