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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public sealed class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task FullApiFlowIsScopedRoleProtectedIdempotentAndProviderBacked()
    {
        var provider = new FakeProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPhoneNumberValidator>();
                services.RemoveAll<ITextMessagingProvider>();
                services.AddSingleton<IPhoneNumberValidator>(provider);
                services.AddSingleton<ITextMessagingProvider>(provider);
            }));
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { mobileNumber = "canonicalize-me" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = ReadInt(await contactResponse.Content.ReadAsStringAsync(), "contactNumberId");
        Assert.IsTrue(contactId > 0);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = ReadInt(await orderResponse.Content.ReadAsStringAsync(), "orderId");

        var forbidden = await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);

        var notificationsResponse = await shopper.GetAsync($"/api/orders/{orderId}/notifications");
        notificationsResponse.EnsureSuccessStatusCode();
        using var notifications = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var dispatch = notifications.RootElement.EnumerateArray().Single(x =>
            x.GetProperty("kind").GetString() == "OrderDispatched");
        var failedNotificationId = dispatch.GetProperty("notificationId").GetInt32();
        var followUp = notifications.RootElement.EnumerateArray().Single(x =>
            x.GetProperty("kind").GetString() == "DeliveryFollowUp");
        var followUpSid = followUp.GetProperty("providerMessageSid").GetString()!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(followUpSid));
        Assert.IsTrue(followUp.GetProperty("scheduledFor").ValueKind == JsonValueKind.String);

        var sendsBeforeResend = provider.SendCount;
        var resend1 = await admin.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-operation" });
        Assert.AreEqual(HttpStatusCode.Created, resend1.StatusCode);
        var resentId = ReadInt(await resend1.Content.ReadAsStringAsync(), "notificationId");
        var resend2 = await admin.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend",
            new { idempotencyKey = "same-operation" });
        Assert.AreEqual(resentId, ReadInt(await resend2.Content.ReadAsStringAsync(), "notificationId"));
        Assert.AreEqual(sendsBeforeResend + 1, provider.SendCount);

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual("canceled", provider.Messages[followUpSid].Status);

        var refreshed = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var resentSid = refreshed.EnumerateArray().Single(x =>
            x.GetProperty("notificationId").GetInt32() == resentId).GetProperty("providerMessageSid").GetString()!;
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{resentId}/content")).StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages[resentSid].Body);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        Assert.IsTrue((await reconciliation.Content.ReadAsStringAsync()).Contains("Matched", StringComparison.Ordinal));

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        var contacts = await shopper.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, contacts.GetArrayLength());
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static int ReadInt(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetInt32();
    }

    private sealed class FakeProvider : IPhoneNumberValidator, ITextMessagingProvider
    {
        private int _sequence;
        public ConcurrentDictionary<string, ProviderMessage> Messages { get; } = new();
        public List<string> NotificationSids { get; } = new();
        public int SendCount => _sequence;

        public Task<PhoneNumberValidationResult> ValidateAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PhoneNumberValidationResult(true, "+15551234567", Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
            CancellationToken cancellationToken = default)
        {
            var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
            var status = sendAt.HasValue ? "scheduled" : body.Contains("on its way", StringComparison.Ordinal) ? "undelivered" : "delivered";
            var message = new ProviderMessage(sid, "+15550000000", to, body, status,
                status == "undelivered" ? 30003 : null, null, DateTimeOffset.UtcNow,
                sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            Messages[sid] = message;
            lock (NotificationSids) { NotificationSids.Add(sid); }
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages[providerMessageSid]);

        public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
        {
            var updated = Messages[providerMessageSid] with { Status = "canceled" };
            Messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessage> RedactAsync(string providerMessageSid, CancellationToken cancellationToken = default)
        {
            var updated = Messages[providerMessageSid] with { Body = string.Empty };
            Messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages.Values.ToList());
    }
}
