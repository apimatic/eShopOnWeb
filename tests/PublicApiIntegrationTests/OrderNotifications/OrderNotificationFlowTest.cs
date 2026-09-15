using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task CompleteFlowIsScopedIdempotentAndCancelsFollowUp()
    {
        var safeDestination = Environment.GetEnvironmentVariable("TWILIO_TEST_TO_NUMBER");
        Assert.IsFalse(string.IsNullOrWhiteSpace(safeDestination), "TWILIO_TEST_TO_NUMBER must be available.");

        var fakeProvider = new FakeMessagingProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMessagingProvider>();
                services.AddSingleton<IMessagingProvider>(fakeProvider);
            });
        });
        using var client = factory.CreateClient();
        UseToken(client, ApiTokenHelper.GetNormalUserToken());

        var contactResponse = await client.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = safeDestination });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = ReadInt(await contactResponse.Content.ReadAsStringAsync(), "contactNumberId");

        var contacts = await client.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(1, contacts!.Length);
        Assert.AreEqual(contactId, contacts[0].GetProperty("contactNumberId").GetInt32());

        UseToken(client, CreateTokenFor("another-shopper@example.test"));
        var otherContacts = await client.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(0, otherContacts!.Length);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);

        UseToken(client, ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrder(client);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);

        UseToken(client, CreateTokenFor("another-shopper@example.test"));
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode);
        var otherOrders = await client.GetFromJsonAsync<JsonElement[]>("/api/my-orders");
        Assert.AreEqual(0, otherOrders!.Length);

        UseToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.IsNotNull(fakeProvider.ScheduledMessageSid);

        UseToken(client, ApiTokenHelper.GetNormalUserToken());
        var notificationPayload = await client.GetStringAsync($"/api/orders/{orderId}/notifications");
        using var notificationDocument = JsonDocument.Parse(notificationPayload);
        var notifications = notificationDocument.RootElement.EnumerateArray().ToArray();
        var placed = notifications.Single(x => x.GetProperty("kind").GetString() == "OrderPlaced");
        var placedNotificationId = placed.GetProperty("notificationId").GetInt32();
        var placedProviderId = placed.GetProperty("providerMessageId").GetString()!;
        fakeProvider.SetStatus(placedProviderId, "undelivered", 30007);

        UseToken(client, ApiTokenHelper.GetAdminUserToken());
        var sendsBeforeResend = fakeProvider.SendCount;
        var firstResend = await client.PostAsJsonAsync($"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "attempt-one" });
        var secondResend = await client.PostAsJsonAsync($"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "attempt-one" });
        Assert.AreEqual(HttpStatusCode.OK, firstResend.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResend.StatusCode);
        var firstResendId = ReadInt(await firstResend.Content.ReadAsStringAsync(), "notificationId");
        var secondResendId = ReadInt(await secondResend.Content.ReadAsStringAsync(), "notificationId");
        Assert.AreEqual(firstResendId, secondResendId);
        Assert.AreEqual(sendsBeforeResend + 1, fakeProvider.SendCount);

        Assert.AreEqual(HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual("canceled", fakeProvider.GetStatus(fakeProvider.ScheduledMessageSid!));

        UseToken(client, ApiTokenHelper.GetNormalUserToken());
        notificationPayload = await client.GetStringAsync($"/api/orders/{orderId}/notifications");
        using var refreshedDocument = JsonDocument.Parse(notificationPayload);
        var resend = refreshedDocument.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == firstResendId);
        var resendProviderId = resend.GetProperty("providerMessageId").GetString()!;

        UseToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/notifications/{firstResendId}/content")).StatusCode);
        Assert.IsNull(fakeProvider.GetBody(resendProviderId));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var reconciliation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.IsTrue(reconciliation.GetProperty("entries").GetArrayLength() > 0);

        UseToken(client, ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        var sendsAfterDelete = fakeProvider.SendCount;
        await PlaceOrder(client);
        Assert.AreEqual(sendsAfterDelete, fakeProvider.SendCount);
    }

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shipToAddress = new
            {
                street = "Test street",
                city = "Test city",
                state = "Test state",
                country = "Canada",
                zipCode = "A1A 1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        return ReadInt(await response.Content.ReadAsStringAsync(), "orderId");
    }

    private static int ReadInt(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetInt32();
    }

    private static void UseToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string CreateTokenFor(string userName)
    {
        var method = typeof(ApiTokenHelper).GetMethod("CreateToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { userName, Array.Empty<string>() })!;
    }

    private sealed class FakeMessagingProvider : IMessagingProvider
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private int _sequence;
        public int SendCount => _sequence;
        public string? ScheduledMessageSid { get; private set; }

        public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber,
            CancellationToken cancellationToken) => Task.FromResult(
                new PhoneNumberValidation(true, phoneNumber, Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
            var now = DateTimeOffset.UtcNow;
            var message = new ProviderMessage(sid, sendAt.HasValue ? "scheduled" : "queued", body,
                null, to, now, sendAt.HasValue ? null : now, null, null);
            _messages[sid] = message;
            if (sendAt.HasValue)
            {
                ScheduledMessageSid = sid;
            }
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var updated = _messages[providerMessageSid] with { Status = "canceled" };
            _messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessage> RedactContentAsync(string providerMessageSid,
            CancellationToken cancellationToken)
        {
            var updated = _messages[providerMessageSid] with { Body = null };
            _messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderMessage>>(
                _messages.Values.Where(x => x.DateSent >= from && x.DateSent <= to).ToList());

        public void SetStatus(string sid, string status, int? errorCode)
        {
            _messages[sid] = _messages[sid] with { Status = status, ErrorCode = errorCode };
        }

        public string GetStatus(string sid) => _messages[sid].Status;
        public string? GetBody(string sid) => _messages[sid].Body;
    }
}
