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
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task ShopperAndOperatorFlowEnforcesOwnershipAndTracksProviderState()
    {
        var twilio = new FakeTwilioMessagingService();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioMessagingService>();
                services.AddSingleton<ITwilioMessagingService>(twilio);
            }));
        using var client = factory.CreateClient();
        var shopperToken = ApiTokenHelper.GetNormalUserToken();
        var adminToken = ApiTokenHelper.GetAdminUserToken();

        var contactResponse = await SendAsync(client, HttpMethod.Post, "/api/contact-numbers",
            new { phoneNumber = "not-canonical" }, shopperToken);
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadJsonAsync(contactResponse);
        var contactNumberId = contact.GetProperty("contactNumberId").GetInt32();
        Assert.AreEqual(FakeTwilioMessagingService.CanonicalNumber, contact.GetProperty("phoneNumber").GetString());

        var adminList = await SendAsync(client, HttpMethod.Get, "/api/contact-numbers", null, adminToken);
        Assert.AreEqual(0, (await ReadJsonAsync(adminList)).GetProperty("contactNumbers").GetArrayLength());
        var crossUserDelete = await SendAsync(client, HttpMethod.Delete, $"/api/contact-numbers/{contactNumberId}", null, adminToken);
        Assert.AreEqual(HttpStatusCode.NotFound, crossUserDelete.StatusCode);

        var orderResponse = await SendAsync(client, HttpMethod.Post, "/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shipToAddress = new { street = "1 Test Way", city = "Toronto", state = "ON", country = "CA", zipCode = "A1A 1A1" }
        }, shopperToken);
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await ReadJsonAsync(orderResponse)).GetProperty("orderId").GetInt32();

        twilio.NextImmediateStatus = "undelivered";
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/dispatch", null, shopperToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/dispatch", null, adminToken)).StatusCode);

        var notificationsResponse = await SendAsync(client, HttpMethod.Get, $"/api/orders/{orderId}/notifications", null, shopperToken);
        var notifications = (await ReadJsonAsync(notificationsResponse)).GetProperty("notifications").EnumerateArray().ToList();
        var placed = notifications.Single(x => x.GetProperty("type").GetString() == "OrderPlaced");
        var dispatched = notifications.Single(x => x.GetProperty("type").GetString() == "OrderDispatched");
        var followUp = notifications.Single(x => x.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("undelivered", dispatched.GetProperty("providerStatus").GetString());
        Assert.AreEqual("scheduled", followUp.GetProperty("providerStatus").GetString());

        twilio.NextImmediateStatus = "delivered";
        var sendCount = twilio.CreateCount;
        var resendOne = await SendAsync(client, HttpMethod.Post,
            $"/api/notifications/{dispatched.GetProperty("notificationId").GetInt32()}/resend",
            new { idempotencyKey = "attempt-1" }, adminToken);
        var resendTwo = await SendAsync(client, HttpMethod.Post,
            $"/api/notifications/{dispatched.GetProperty("notificationId").GetInt32()}/resend",
            new { idempotencyKey = "attempt-1" }, adminToken);
        var resendId = (await ReadJsonAsync(resendOne)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(resendId, (await ReadJsonAsync(resendTwo)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sendCount + 1, twilio.CreateCount);

        twilio.NextImmediateStatus = "undelivered";
        Assert.AreEqual(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/cancel", null, adminToken)).StatusCode);
        notificationsResponse = await SendAsync(client, HttpMethod.Get, $"/api/orders/{orderId}/notifications", null, shopperToken);
        notifications = (await ReadJsonAsync(notificationsResponse)).GetProperty("notifications").EnumerateArray().ToList();
        followUp = notifications.Single(x => x.GetProperty("type").GetString() == "DeliveryFollowUp");
        var cancellationNotice = notifications.Single(x => x.GetProperty("type").GetString() == "OrderCancelled");
        Assert.AreEqual("canceled", followUp.GetProperty("providerStatus").GetString());
        twilio.NextImmediateStatus = "delivered";
        Assert.AreEqual(HttpStatusCode.Created,
            (await SendAsync(client, HttpMethod.Post,
                $"/api/notifications/{cancellationNotice.GetProperty("notificationId").GetInt32()}/resend",
                new { idempotencyKey = "cancellation-attempt-1" }, adminToken)).StatusCode);

        var placedId = placed.GetProperty("notificationId").GetInt32();
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Delete, $"/api/notifications/{placedId}/content", null, shopperToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, $"/api/notifications/{placedId}/content", null, adminToken)).StatusCode);
        notificationsResponse = await SendAsync(client, HttpMethod.Get, $"/api/orders/{orderId}/notifications", null, shopperToken);
        placed = (await ReadJsonAsync(notificationsResponse)).GetProperty("notifications").EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == placedId);
        Assert.AreEqual(JsonValueKind.Null, placed.GetProperty("body").ValueKind);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await SendAsync(client, HttpMethod.Get,
            $"/api/notifications/reconciliation?from={from}&to={to}", null, adminToken);
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        Assert.IsTrue((await ReadJsonAsync(reconciliation)).GetProperty("messages").GetArrayLength() > 0);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, $"/api/notifications/reconciliation?from={from}&to={to}", null, shopperToken)).StatusCode);

        twilio.FailNextCreate = true;
        var providerFailureOrder = await SendAsync(client, HttpMethod.Post, "/api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } },
            shipToAddress = new { street = "1 Test Way", city = "Toronto", state = "ON", country = "CA", zipCode = "A1A 1A1" }
        }, shopperToken);
        Assert.AreEqual(HttpStatusCode.Created, providerFailureOrder.StatusCode);
        var providerFailureOrderId = (await ReadJsonAsync(providerFailureOrder)).GetProperty("orderId").GetInt32();
        var failedNotifications = await SendAsync(client, HttpMethod.Get,
            $"/api/orders/{providerFailureOrderId}/notifications", null, shopperToken);
        Assert.AreEqual("failed", (await ReadJsonAsync(failedNotifications)).GetProperty("notifications")[0].GetProperty("providerStatus").GetString());

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, $"/api/contact-numbers/{contactNumberId}", null, shopperToken)).StatusCode);
        var shopperList = await SendAsync(client, HttpMethod.Get, "/api/contact-numbers", null, shopperToken);
        Assert.AreEqual(0, (await ReadJsonAsync(shopperList)).GetProperty("contactNumbers").GetArrayLength());

        var noContactOrder = await SendAsync(client, HttpMethod.Post, "/api/orders", new
        {
            items = new[] { new { catalogItemId = 3, quantity = 1 } },
            shipToAddress = new { street = "1 Test Way", city = "Toronto", state = "ON", country = "CA", zipCode = "A1A 1A1" }
        }, shopperToken);
        Assert.AreEqual(HttpStatusCode.Created, noContactOrder.StatusCode);
        var noContactOrderId = (await ReadJsonAsync(noContactOrder)).GetProperty("orderId").GetInt32();
        var noContactNotifications = await SendAsync(client, HttpMethod.Get,
            $"/api/orders/{noContactOrderId}/notifications", null, shopperToken);
        Assert.AreEqual(0, (await ReadJsonAsync(noContactNotifications)).GetProperty("notifications").GetArrayLength());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string uri, object? body, string token)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed class FakeTwilioMessagingService : ITwilioMessagingService
    {
        public const string CanonicalNumber = "+15550002222";
        private readonly ConcurrentDictionary<string, TwilioMessageState> _messages = new();
        private int _sequence;
        public string NextImmediateStatus { get; set; } = "delivered";
        public bool FailNextCreate { get; set; }
        public int CreateCount => _sequence;

        public Task<string?> ValidateAndNormalizeAsync(string phoneNumber, CancellationToken cancellationToken) => Task.FromResult<string?>(CanonicalNumber);
        public Task<TwilioMessageState> SendAsync(string to, string body, CancellationToken cancellationToken) => Task.FromResult(Create(body, NextImmediateStatus, false));
        public Task<TwilioMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) => Task.FromResult(Create(body, "scheduled", true));
        public Task<TwilioMessageState> FetchAsync(string messageSid, CancellationToken cancellationToken) => Task.FromResult(_messages[messageSid]);

        public Task<TwilioMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = _messages[messageSid];
            var updated = current with { Status = "canceled" };
            _messages[messageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<TwilioMessageState> RedactAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = _messages[messageSid];
            var updated = current with { Body = string.Empty };
            _messages[messageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<TwilioMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            IReadOnlyList<TwilioMessageState> result = _messages.Values
                .Where(x => x.DateSent >= from && x.DateSent <= to)
                .ToList();
            return Task.FromResult(result);
        }

        private TwilioMessageState Create(string body, string status, bool scheduled)
        {
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new TwilioApiException(HttpStatusCode.BadGateway, 30001);
            }

            var sid = $"SM{Interlocked.Increment(ref _sequence):X32}";
            var now = DateTimeOffset.UtcNow;
            var message = new TwilioMessageState(sid, status, status == "undelivered" ? 30003 : null, now, scheduled ? null : now, body);
            _messages[sid] = message;
            return message;
        }
    }
}
