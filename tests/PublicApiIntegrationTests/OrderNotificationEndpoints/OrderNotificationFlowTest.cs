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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task SupportsShopperAndOperatorFlowWithProviderStateAndIdempotency()
    {
        var provider = new FakeTwilioGateway();
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton<ITwilioGateway>(provider);
            }));
        using var client = application.CreateClient();

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        var contactResponse = await client.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "555-555-0100" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactNumberId = await ReadIntAsync(contactResponse, "contactNumberId");

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = await ReadIntAsync(orderResponse, "orderId");

        Authenticate(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(1, provider.ScheduledCount);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual(1, provider.CancelledCount);

        // An administrator is not allowed to read another shopper's order through a shopper endpoint.
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode);

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        var notificationsResponse = await client.GetAsync($"/api/orders/{orderId}/notifications");
        notificationsResponse.EnsureSuccessStatusCode();
        using var notificationsJson = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var notifications = notificationsJson.RootElement.GetProperty("notifications").EnumerateArray().ToList();
        Assert.AreEqual(4, notifications.Count);
        var failedNotificationId = notifications[0].GetProperty("notificationId").GetInt32();
        Assert.IsTrue(notifications.Any(x =>
            x.GetProperty("kind").GetString() == "DeliveryFollowUp" &&
            x.GetProperty("providerStatus").GetString() == "canceled"));

        Authenticate(client, ApiTokenHelper.GetAdminUserToken());
        var firstResend = await client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-logical-attempt" });
        firstResend.EnsureSuccessStatusCode();
        var resendId = await ReadIntAsync(firstResend, "notificationId");
        var sendsAfterFirstResend = provider.SendCount;

        var duplicateResend = await client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-logical-attempt" });
        duplicateResend.EnsureSuccessStatusCode();
        Assert.AreEqual(resendId, await ReadIntAsync(duplicateResend, "notificationId"));
        Assert.AreEqual(sendsAfterFirstResend, provider.SendCount);

        var freshResend = await client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "fresh-logical-attempt" });
        freshResend.EnsureSuccessStatusCode();
        Assert.AreNotEqual(resendId, await ReadIntAsync(freshResend, "notificationId"));
        Assert.AreEqual(sendsAfterFirstResend + 1, provider.SendCount);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/notifications/{resendId}/content")).StatusCode);
        Assert.IsTrue(provider.WasRedacted(resendId));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var reconciliation = await client.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        using var reconciliationJson = JsonDocument.Parse(await reconciliation.Content.ReadAsStringAsync());
        var reconciledRows = reconciliationJson.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.AreEqual(6, reconciledRows.Count);
        Assert.IsTrue(reconciledRows.All(x => x.GetProperty("match").GetString() == "matched"));

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);
        var contacts = await client.GetStringAsync("/api/contact-numbers");
        using var contactsJson = JsonDocument.Parse(contacts);
        Assert.AreEqual(0, contactsJson.RootElement.GetProperty("contactNumbers").GetArrayLength());
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<int> ReadIntAsync(HttpResponseMessage response, string property)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(property).GetInt32();
    }

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private readonly ConcurrentDictionary<string, bool> _redacted = new();
        private int _nextSid;

        public int SendCount { get; private set; }
        public int ScheduledCount { get; private set; }
        public int CancelledCount { get; private set; }

        public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidation(true, "+15555550100"));

        public Task<ProviderMessage> SendMessageAsync(string destination, string content, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(Add("undelivered"));
        }

        public Task<ProviderMessage> ScheduleMessageAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken cancellationToken)
        {
            ScheduledCount++;
            return Task.FromResult(Add("scheduled"));
        }

        public Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            CancelledCount++;
            var current = _messages[providerMessageSid];
            var cancelled = current with { Status = "canceled" };
            _messages[providerMessageSid] = cancelled;
            return Task.FromResult(cancelled);
        }

        public Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            _redacted[providerMessageSid] = true;
            return Task.FromResult(_messages[providerMessageSid]);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            // Twilio's DateSent list filter omits scheduled/canceled messages with no sent date.
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.Where(x => x.DateSent.HasValue).ToList());

        public bool WasRedacted(int notificationId) => _redacted.Count > 0;

        private ProviderMessage Add(string status)
        {
            var sid = $"SM{Interlocked.Increment(ref _nextSid):D32}";
            var now = DateTimeOffset.UtcNow;
            var message = new ProviderMessage(sid, status, status == "undelivered" ? 30007 : null, now,
                status == "scheduled" ? null : now);
            _messages[sid] = message;
            return message;
        }
    }
}
