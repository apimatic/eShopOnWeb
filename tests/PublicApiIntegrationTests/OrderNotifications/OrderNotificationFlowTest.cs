using System;
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
using Microsoft.eShopWeb.PublicApi.OrderNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public sealed class OrderNotificationFlowTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task DrivesCompleteFlowWithOwnershipIdempotencyCancellationAndReconciliation()
    {
        var provider = new RecordingMessageProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITextMessageProvider>();
                services.AddSingleton<ITextMessageProvider>(provider);
            }));

        using var shopper = AuthorizedClient(factory, ApiTokenHelper.GetNormalUserToken("notification-shopper@example.test"));
        using var otherShopper = AuthorizedClient(factory, ApiTokenHelper.GetNormalUserToken("another-shopper@example.test"));
        using var administrator = AuthorizedClient(factory, ApiTokenHelper.GetAdminUserToken());

        var register = await shopper.PostAsJsonAsync("/api/contact-numbers", new RegisterContactNumberRequest("not-canonical"));
        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<RegisterContactNumberResponse>(JsonOptions);
        Assert.IsNotNull(registered);
        Assert.AreEqual(RecordingMessageProvider.CanonicalNumber, registered.CanonicalNumber);

        var place = await shopper.PostAsJsonAsync("/api/orders", ValidOrder());
        Assert.AreEqual(HttpStatusCode.Created, place.StatusCode);
        var placed = await place.Content.ReadFromJsonAsync<PlaceOrderResponse>(JsonOptions);
        Assert.IsNotNull(placed);
        Assert.IsTrue(placed.OrderId > 0);

        var forbiddenOrderRead = await otherShopper.GetAsync($"/api/orders/{placed.OrderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, forbiddenOrderRead.StatusCode);

        var notificationsResponse = await shopper.GetAsync($"/api/orders/{placed.OrderId}/notifications");
        notificationsResponse.EnsureSuccessStatusCode();
        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<OrderNotificationsResponse>(JsonOptions);
        Assert.IsNotNull(notifications);
        var failedPlaced = notifications.Notifications.Single(x => x.Kind == "OrderPlaced");
        Assert.AreEqual("undelivered", failedPlaced.ProviderStatus);

        var shopperDispatch = await shopper.PostAsync($"/api/orders/{placed.OrderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperDispatch.StatusCode);

        (await administrator.PostAsync($"/api/orders/{placed.OrderId}/dispatch", null)).EnsureSuccessStatusCode();
        var afterDispatch = await GetNotificationsAsync(shopper, placed.OrderId);
        var followUp = afterDispatch.Notifications.Single(x => x.Kind == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.ProviderStatus);
        Assert.IsTrue(followUp.ScheduledFor > DateTimeOffset.UtcNow.AddDays(2));

        (await administrator.PostAsync($"/api/orders/{placed.OrderId}/cancel", null)).EnsureSuccessStatusCode();
        var afterCancel = await GetNotificationsAsync(shopper, placed.OrderId);
        Assert.AreEqual("canceled", afterCancel.Notifications.Single(x => x.NotificationId == followUp.NotificationId).ProviderStatus);
        Assert.AreEqual(1, provider.CancelCalls);

        var sendCallsBeforeResend = provider.SendCalls;
        var resendRequest = new ResendNotificationRequest("same-caller-key");
        var resendOne = await administrator.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend", resendRequest);
        resendOne.EnsureSuccessStatusCode();
        var resentOne = await resendOne.Content.ReadFromJsonAsync<ResendNotificationResponse>(JsonOptions);
        Assert.IsNotNull(resentOne);

        var resendTwo = await administrator.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend", resendRequest);
        resendTwo.EnsureSuccessStatusCode();
        var resentTwo = await resendTwo.Content.ReadFromJsonAsync<ResendNotificationResponse>(JsonOptions);
        Assert.IsNotNull(resentTwo);
        Assert.AreEqual(resentOne.NotificationId, resentTwo.NotificationId);
        Assert.AreEqual(sendCallsBeforeResend + 1, provider.SendCalls);

        var freshResend = await administrator.PostAsJsonAsync(
            $"/api/notifications/{failedPlaced.NotificationId}/resend",
            new ResendNotificationRequest("fresh-caller-key"));
        freshResend.EnsureSuccessStatusCode();
        var freshResult = await freshResend.Content.ReadFromJsonAsync<ResendNotificationResponse>(JsonOptions);
        Assert.IsNotNull(freshResult);
        Assert.AreNotEqual(resentOne.NotificationId, freshResult.NotificationId);
        Assert.AreEqual(sendCallsBeforeResend + 2, provider.SendCalls);

        var dispose = await administrator.DeleteAsync($"/api/notifications/{resentOne.NotificationId}/content");
        dispose.EnsureSuccessStatusCode();
        var disposed = await dispose.Content.ReadFromJsonAsync<ContentDisposalResponse>(JsonOptions);
        Assert.IsNotNull(disposed);
        Assert.IsTrue(disposed.ContentDisposed);
        Assert.AreEqual(1, provider.RedactCalls);
        Assert.IsTrue(string.IsNullOrEmpty(provider.GetBody(resentOne.NotificationId)));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await administrator.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        var report = await reconciliation.Content.ReadFromJsonAsync<ReconciliationResponse>(JsonOptions);
        Assert.IsNotNull(report);
        Assert.IsTrue(report.Messages.Count > 0);
        Assert.IsTrue(report.Messages.All(x => x.Match == "matched"));

        var callsBeforeDelete = provider.SendCalls;
        var delete = await shopper.DeleteAsync($"/api/contact-numbers/{registered.ContactNumberId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        (await shopper.PostAsJsonAsync("/api/orders", ValidOrder())).EnsureSuccessStatusCode();
        Assert.AreEqual(callsBeforeDelete, provider.SendCalls);
    }

    private static PlaceOrderRequest ValidOrder() => new(
        new[] { new OrderLineRequest(1, 1) },
        new ShippingAddressRequest("1 Test Street", "Test City", "", "CA", "A1A1A1"));

    private static async Task<OrderNotificationsResponse> GetNotificationsAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"/api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderNotificationsResponse>(JsonOptions))!;
    }

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class RecordingMessageProvider : ITextMessageProvider
    {
        public const string CanonicalNumber = "+10000000000";
        private readonly object _gate = new();
        private readonly Dictionary<string, ProviderMessageSnapshot> _messages = new(StringComparer.Ordinal);
        private int _nextSid;
        private int _sendCalls;

        public int SendCalls => _sendCalls;
        public int CancelCalls { get; private set; }
        public int RedactCalls { get; private set; }

        public Task<string?> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(CanonicalNumber);

        public Task<ProviderMessageSnapshot> SendAsync(string destination, string body, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _sendCalls++;
                return Task.FromResult(Add(body, _sendCalls == 1 ? "undelivered" : "delivered", 30003));
            }
        }

        public Task<ProviderMessageSnapshot> ScheduleAsync(string destination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(Add(body, "scheduled", null));
            }
        }

        public Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                CancelCalls++;
                return Task.FromResult(Update(providerMessageSid, status: "canceled"));
            }
        }

        public Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_messages[providerMessageSid]);
            }
        }

        public Task<ProviderMessageSnapshot> RedactAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                RedactCalls++;
                return Task.FromResult(Update(providerMessageSid, body: string.Empty));
            }
        }

        public Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ProviderMessageSnapshot>>(_messages.Values.ToList());
            }
        }

        public string? GetBody(int notificationId)
        {
            lock (_gate)
            {
                return _messages.Values.Single(x => x.Sid == $"SM{notificationId:D6}").Body;
            }
        }

        private ProviderMessageSnapshot Add(string body, string status, int? errorCode)
        {
            var sid = $"SM{++_nextSid:D6}";
            var now = DateTimeOffset.UtcNow.ToString("O");
            var message = new ProviderMessageSnapshot(sid, status, "outbound-api", body, errorCode, null, now, now, now);
            _messages[sid] = message;
            return message;
        }

        private ProviderMessageSnapshot Update(string sid, string? status = null, string? body = null)
        {
            var current = _messages[sid];
            var updated = current with
            {
                Status = status ?? current.Status,
                Body = body ?? current.Body,
                DateUpdated = DateTimeOffset.UtcNow.ToString("O")
            };
            if (body == string.Empty)
            {
                updated = updated with { Body = string.Empty };
            }
            _messages[sid] = updated;
            return updated;
        }
    }
}
