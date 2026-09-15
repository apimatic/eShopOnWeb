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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task EntireFlowIsApiDrivableScopedAndIdempotent()
    {
        var provider = new FakeTwilioProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioMessageProvider>();
                services.AddSingleton<ITwilioMessageProvider>(provider);
            }));
        using var client = factory.CreateClient();
        SetToken(client, ApiTokenHelper.GetNormalUserToken());

        var contactResponse = await client.PostAsJsonAsync("/api/contact-numbers", new { number = "(416) 555-0100" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = await ReadIntAsync(contactResponse, "contactNumberId");

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Street", city = "Toronto", state = "ON", country = "Canada", zipCode = "M5V 1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = await ReadIntAsync(orderResponse, "orderId");

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        var wrongShopper = await client.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongShopper.StatusCode);

        var dispatch = await client.PostAsync($"/api/orders/{orderId}/dispatch", null);
        dispatch.EnsureSuccessStatusCode();
        Assert.AreEqual(1, provider.ScheduledCount);

        var cancel = await client.PostAsync($"/api/orders/{orderId}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        Assert.AreEqual(1, provider.CancelledCount);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var notificationsResponse = await client.GetAsync($"/api/orders/{orderId}/notifications");
        notificationsResponse.EnsureSuccessStatusCode();
        var notificationsJson = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var notifications = notificationsJson.RootElement.GetProperty("notifications");
        var failedNotificationId = notifications.EnumerateArray()
            .First(x => x.GetProperty("providerStatus").GetString() == "undelivered")
            .GetProperty("notificationId").GetInt32();
        Assert.IsTrue(notifications.EnumerateArray().Any(x => x.GetProperty("providerStatus").GetString() == "canceled"));

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        var resend1 = await client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "attempt-1" });
        resend1.EnsureSuccessStatusCode();
        var resendId1 = await ReadIntAsync(resend1, "notificationId");
        var sendsAfterFirstResend = provider.SentCount;
        var resend2 = await client.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "attempt-1" });
        resend2.EnsureSuccessStatusCode();
        Assert.AreEqual(resendId1, await ReadIntAsync(resend2, "notificationId"));
        Assert.AreEqual(sendsAfterFirstResend, provider.SentCount);

        var dispose = await client.DeleteAsync($"/api/notifications/{failedNotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.IsTrue(provider.DisposedCount > 0);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await client.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        var reconciliationJson = await reconciliation.Content.ReadAsStringAsync();
        Assert.IsTrue(JsonDocument.Parse(reconciliationJson).RootElement.GetProperty("messages").GetArrayLength() > 0);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var remove = await client.DeleteAsync($"/api/contact-numbers/{contactId}");
        Assert.AreEqual(HttpStatusCode.NoContent, remove.StatusCode);
        var contacts = await client.GetStringAsync("/api/contact-numbers");
        Assert.AreEqual(0, JsonDocument.Parse(contacts).RootElement.GetProperty("contactNumbers").GetArrayLength());
    }

    private static void SetToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<int> ReadIntAsync(HttpResponseMessage response, string property)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(property).GetInt32();
    }

    private sealed class FakeTwilioProvider : ITwilioMessageProvider
    {
        private readonly ConcurrentDictionary<string, ProviderMessageState> _messages = new();
        private int _next;
        public int SentCount { get; private set; }
        public int ScheduledCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int DisposedCount { get; private set; }

        public Task<PhoneValidationResult> ValidateDestinationAsync(string number, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneValidationResult(true, "+14165550100"));

        public Task<ProviderMessageState> SendAsync(string canonicalNumber, string body, CancellationToken cancellationToken)
        {
            SentCount++;
            return Task.FromResult(Add("undelivered", body));
        }

        public Task<ProviderMessageState> ScheduleAsync(string canonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        {
            ScheduledCount++;
            var state = Add("scheduled", body) with { ScheduledFor = sendAt };
            _messages[state.Sid!] = state;
            return Task.FromResult(state);
        }

        public Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            CancelledCount++;
            var state = _messages[providerMessageSid] with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerMessageSid] = state;
            return Task.FromResult(state);
        }

        public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            DisposedCount++;
            var state = _messages[providerMessageSid] with { Body = string.Empty, DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerMessageSid] = state;
            return Task.FromResult(state);
        }

        public Task<IReadOnlyList<ProviderMessageRecord>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessageRecord>>(_messages.Values.Select(x => new ProviderMessageRecord(
                x.Sid!, x.Status, x.DateCreated, x.DateUpdated, x.DateSent, x.ErrorCode, x.ErrorMessage)).ToList());

        private ProviderMessageState Add(string status, string body)
        {
            var sid = $"SM{Interlocked.Increment(ref _next):D8}";
            var now = DateTimeOffset.UtcNow;
            var state = new ProviderMessageState(sid, status, null, null, now, now, now, Body: body);
            _messages[sid] = state;
            return state;
        }
    }
}
