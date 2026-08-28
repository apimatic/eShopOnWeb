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
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    private NotificationApiFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new NotificationApiFactory();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task FullFlowPreservesOwnershipAndProviderState()
    {
        Authorize(ApiTokenHelper.GetNormalUserToken());
        var contactResponse = await _client.PostAsJsonAsync("/api/contact-numbers", new
        {
            phoneNumber = "provider-fixture"
        });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadJsonAsync(contactResponse);
        var contactNumberId = contact.RootElement.GetProperty("contactNumberId").GetInt32();
        Assert.AreEqual("provider-canonical", contact.RootElement.GetProperty("phoneNumber").GetString());

        _factory.Provider.FailNextImmediate = true;
        var orderResponse = await _client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Street",
                city = "Test City",
                state = "",
                country = "CA",
                zipCode = "A1A1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await ReadJsonAsync(orderResponse);
        var orderId = order.RootElement.GetProperty("orderId").GetInt32();

        var notificationsResponse = await _client.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.OK, notificationsResponse.StatusCode);
        var notifications = await ReadJsonAsync(notificationsResponse);
        var failedNotificationId = notifications.RootElement[0].GetProperty("notificationId").GetInt32();
        Assert.AreEqual("undelivered", notifications.RootElement[0].GetProperty("status").GetString());

        var forbiddenDispatch = await _client.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);

        Authorize(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            (await _client.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);
        var resendOne = await _client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "same-attempt"
        });
        var resendTwo = await _client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "same-attempt"
        });
        Assert.AreEqual(HttpStatusCode.Created, resendOne.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, resendTwo.StatusCode);
        var resendOneJson = await ReadJsonAsync(resendOne);
        var resendTwoJson = await ReadJsonAsync(resendTwo);
        Assert.AreEqual(
            resendOneJson.RootElement.GetProperty("notificationId").GetInt32(),
            resendTwoJson.RootElement.GetProperty("notificationId").GetInt32());
        Assert.AreEqual(2, _factory.Provider.SendCount);

        var freshResend = await _client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "fresh-attempt"
        });
        Assert.AreEqual(HttpStatusCode.Created, freshResend.StatusCode);
        var freshResendJson = await ReadJsonAsync(freshResend);
        Assert.AreNotEqual(
            resendOneJson.RootElement.GetProperty("notificationId").GetInt32(),
            freshResendJson.RootElement.GetProperty("notificationId").GetInt32());
        Assert.AreEqual(3, _factory.Provider.SendCount);

        Assert.AreEqual(HttpStatusCode.OK, (await _client.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(1, _factory.Provider.ScheduledCount);
        Assert.AreEqual(HttpStatusCode.OK, (await _client.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual(1, _factory.Provider.CancelCount);

        var disposeResponse = await _client.DeleteAsync($"/api/notifications/{failedNotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, disposeResponse.StatusCode);
        Assert.AreEqual(1, _factory.Provider.RedactCount);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await _client.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        var reconciliationJson = await ReadJsonAsync(reconciliation);
        Assert.IsTrue(reconciliationJson.RootElement.GetProperty("entries").GetArrayLength() >= 4);

        Authorize(ApiTokenHelper.GetNormalUserToken());
        notificationsResponse = await _client.GetAsync($"/api/orders/{orderId}/notifications");
        notifications = await ReadJsonAsync(notificationsResponse);
        var original = notifications.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("notificationId").GetInt32() == failedNotificationId);
        Assert.AreEqual(JsonValueKind.Null, original.GetProperty("content").ValueKind);
        var followUp = notifications.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", followUp.GetProperty("status").GetString());

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);
        var contacts = await ReadJsonAsync(await _client.GetAsync("/api/contact-numbers"));
        Assert.AreEqual(0, contacts.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task ProviderSendFailureDoesNotRollBackOrder()
    {
        Authorize(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(
            HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "provider-fixture" })).StatusCode);
        _factory.Provider.ThrowNextSend = true;

        var orderResponse = await _client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new
            {
                street = "1 Test Street",
                city = "Test City",
                state = "",
                country = "CA",
                zipCode = "A1A1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await ReadJsonAsync(orderResponse);
        var orderId = order.RootElement.GetProperty("orderId").GetInt32();

        var notifications = await ReadJsonAsync(await _client.GetAsync($"/api/orders/{orderId}/notifications"));
        Assert.AreEqual("send-failed", notifications.RootElement[0].GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, notifications.RootElement[0].GetProperty("providerMessageSid").ValueKind);
        var orders = await ReadJsonAsync(await _client.GetAsync("/api/my-orders"));
        Assert.AreEqual(orderId, orders.RootElement[0].GetProperty("orderId").GetInt32());
        Assert.AreEqual("placed", orders.RootElement[0].GetProperty("status").GetString());
    }

    private void Authorize(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        public FakeTwilioMessagingClient Provider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("UseOnlyInMemoryDatabase", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITwilioMessagingClient>();
                services.AddSingleton<ITwilioMessagingClient>(Provider);
            });
        }
    }

    private sealed class FakeTwilioMessagingClient : ITwilioMessagingClient
    {
        private readonly ConcurrentDictionary<string, TwilioMessage> _messages = new();
        private int _sequence;

        public bool FailNextImmediate { get; set; }
        public bool ThrowNextSend { get; set; }
        public int SendCount { get; private set; }
        public int ScheduledCount { get; private set; }
        public int CancelCount { get; private set; }
        public int RedactCount { get; private set; }

        public Task<PhoneNumberLookup> LookupPhoneNumberAsync(
            string phoneNumber,
            string? countryCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberLookup(true, "provider-canonical", Array.Empty<string>()));

        public Task<TwilioMessage> SendMessageAsync(
            string to,
            string body,
            DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            SendCount++;
            if (ThrowNextSend)
            {
                ThrowNextSend = false;
                throw new TwilioApiException(400, 21610);
            }
            var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
            var status = sendAt.HasValue ? "scheduled" : FailNextImmediate ? "undelivered" : "delivered";
            int? errorCode = status == "undelivered" ? 30007 : null;
            FailNextImmediate = false;
            if (sendAt.HasValue)
            {
                ScheduledCount++;
            }

            var now = DateTimeOffset.UtcNow;
            var message = new TwilioMessage(sid, status, body, errorCode, now, sendAt.HasValue ? null : now);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            CancelCount++;
            var current = _messages[messageSid];
            var canceled = current with { Status = "canceled" };
            _messages[messageSid] = canceled;
            return Task.FromResult(canceled);
        }

        public Task<TwilioMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            RedactCount++;
            var current = _messages[messageSid];
            var redacted = current with { Body = string.Empty };
            _messages[messageSid] = redacted;
            return Task.FromResult(redacted);
        }

        public Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TwilioMessage> result = _messages.Values
                .Where(message => (message.DateSent ?? message.DateCreated) is { } date && date >= from && date <= to)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
