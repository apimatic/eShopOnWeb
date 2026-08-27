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

namespace PublicApiIntegrationTests.NotificationFlows;

[TestClass]
public class NotificationFlowTests
{
    [TestMethod]
    public async Task ApiDrivesPlacementDispatchCancellationResendDisposalAndReconciliation()
    {
        var provider = new FakeTwilioMessagingClient();
        await using var factory = new NotificationApiFactory(provider);
        using var shopper = CreateClient(factory, ApiTokenHelper.GetNormalUserToken());
        using var admin = CreateClient(factory, ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new
        {
            phoneNumber = "+1 (555) 123-4567"
        });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactNumberId = ReadInt(await contactResponse.Content.ReadAsStringAsync(), "contactNumberId");

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", ValidOrderRequest());
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = ReadInt(await orderResponse.Content.ReadAsStringAsync(), "orderId");
        Assert.AreEqual(1, provider.SendCount);

        var dispatchResponse = await admin.PostAsync($"/api/orders/{orderId}/dispatch", null);
        dispatchResponse.EnsureSuccessStatusCode();
        Assert.AreEqual(3, provider.SendCount);

        var beforeCancellation = await ReadNotifications(shopper, orderId);
        var followUp = beforeCancellation.Single(x => x.GetProperty("type").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.GetProperty("providerStatus").GetString());

        var cancelResponse = await admin.PostAsync($"/api/orders/{orderId}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();
        var afterCancellation = await ReadNotifications(shopper, orderId);
        Assert.AreEqual(
            "canceled",
            afterCancellation.Single(x => x.GetProperty("type").GetString() == "DeliveryFollowUp")
                .GetProperty("providerStatus").GetString());

        provider.RejectNextSend = true;
        var failedOrderResponse = await shopper.PostAsJsonAsync("/api/orders", ValidOrderRequest());
        failedOrderResponse.EnsureSuccessStatusCode();
        var failedOrderId = ReadInt(await failedOrderResponse.Content.ReadAsStringAsync(), "orderId");
        var failed = (await ReadNotifications(shopper, failedOrderId)).Single();
        Assert.AreEqual("provider_rejected", failed.GetProperty("providerStatus").GetString());
        var failedNotificationId = failed.GetProperty("notificationId").GetInt32();

        var sendsBeforeResend = provider.SendCount;
        var resendResponse = await admin.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "attempt-1"
        });
        Assert.AreEqual(HttpStatusCode.Created, resendResponse.StatusCode);
        var resentNotificationId = ReadInt(await resendResponse.Content.ReadAsStringAsync(), "notificationId");

        var duplicateResponse = await admin.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "attempt-1"
        });
        duplicateResponse.EnsureSuccessStatusCode();
        Assert.AreEqual(resentNotificationId, ReadInt(await duplicateResponse.Content.ReadAsStringAsync(), "notificationId"));
        Assert.AreEqual(sendsBeforeResend + 1, provider.SendCount);

        var disposalResponse = await admin.DeleteAsync($"/api/notifications/{resentNotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, disposalResponse.StatusCode);
        var disposed = (await ReadNotifications(shopper, failedOrderId))
            .Single(x => x.GetProperty("notificationId").GetInt32() == resentNotificationId);
        Assert.AreEqual(JsonValueKind.Null, disposed.GetProperty("content").ValueKind);
        Assert.IsTrue(disposed.GetProperty("contentDisposed").GetBoolean());

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        using (var report = JsonDocument.Parse(await reconciliation.Content.ReadAsStringAsync()))
        {
            Assert.IsTrue(report.RootElement.GetProperty("entries").EnumerateArray()
                .Any(x => x.GetProperty("match").GetString() == "matched"));
        }

        var deleteResponse = await shopper.DeleteAsync($"/api/contact-numbers/{contactNumberId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var numbers = await shopper.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, numbers.GetArrayLength());
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ValidOrderRequest() => new
    {
        items = new[] { new { catalogItemId = 1, quantity = 1 } },
        shippingAddress = new
        {
            street = "1 Test Street",
            city = "Toronto",
            state = "ON",
            country = "Canada",
            zipCode = "M5V 1A1"
        }
    };

    private static async Task<List<JsonElement>> ReadNotifications(HttpClient client, int orderId)
    {
        var response = await client.GetAsync($"/api/orders/{orderId}/notifications");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static int ReadInt(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetInt32();
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakeTwilioMessagingClient _provider;

        public NotificationApiFactory(FakeTwilioMessagingClient provider)
        {
            _provider = provider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioMessagingClient>();
                services.AddSingleton<ITwilioMessagingClient>(_provider);
            });
        }
    }

    private sealed class FakeTwilioMessagingClient : ITwilioMessagingClient
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private int _sequence;

        public bool RejectNextSend { get; set; }
        public int SendCount { get; private set; }

        public Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidatedPhoneNumber(true, "+15551234567", Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            SendCount++;
            if (RejectNextSend)
            {
                RejectNextSend = false;
                throw new TwilioProviderException("test send", 400, 30007);
            }

            var message = new ProviderMessage(
                $"SM{Interlocked.Increment(ref _sequence):D32}",
                sendAt.HasValue ? "scheduled" : "delivered",
                null,
                DateTimeOffset.UtcNow,
                sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            _messages[message.Sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = _messages[messageSid];
            var cancelled = current with { Status = "canceled" };
            _messages[messageSid] = cancelled;
            return Task.FromResult(cancelled);
        }

        public Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values
                .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
                .ToList());
    }
}
