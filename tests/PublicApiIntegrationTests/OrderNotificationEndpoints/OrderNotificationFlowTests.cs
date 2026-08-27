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

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public sealed class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task FullFlowSchedulesCancelsRedactsReconcilesAndResendsIdempotently()
    {
        var provider = new FakeTwilioGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton<ITwilioGateway>(provider);
            }));
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("api/contact-numbers",
            new { phoneNumber = "ignored-by-fake" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = (await contactResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contactNumberId").GetInt32();

        var orderResponse = await shopper.PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        Assert.AreEqual(1, provider.SendCount);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(3, provider.SendCount);
        Assert.AreEqual(1, provider.Messages.Values.Count(x => x.Status == "scheduled"));

        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual(1, provider.Messages.Values.Count(x => x.Status == "canceled"));

        var notifications = await shopper.GetFromJsonAsync<JsonElement>($"api/orders/{orderId}/notifications");
        var placed = notifications.EnumerateArray().First(x => x.GetProperty("kind").GetInt32() == 0);
        var placedId = placed.GetProperty("notificationId").GetInt32();
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"api/notifications/{placedId}/content")).StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages[placed.GetProperty("providerMessageSid").GetString()!].Body);

        provider.FailNext = true;
        var failedOrderResponse = await shopper.PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 2, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, failedOrderResponse.StatusCode,
            "Provider delivery failure must not fail order placement.");
        var failedOrderId = (await failedOrderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        var failedNotifications = await shopper.GetFromJsonAsync<JsonElement>($"api/orders/{failedOrderId}/notifications");
        var failedId = failedNotifications.EnumerateArray().Single().GetProperty("notificationId").GetInt32();

        var sendsBeforeResend = provider.SendCount;
        var resend1 = await admin.PostAsJsonAsync($"api/notifications/{failedId}/resend",
            new { idempotencyKey = "same-request" });
        var resend2 = await admin.PostAsJsonAsync($"api/notifications/{failedId}/resend",
            new { idempotencyKey = "same-request" });
        Assert.AreEqual(HttpStatusCode.Created, resend1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, resend2.StatusCode);
        var resend1Id = (await resend1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetInt32();
        var resend2Id = (await resend2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(resend1Id, resend2Id);
        Assert.AreEqual(sendsBeforeResend + 1, provider.SendCount);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reportResponse = await admin.GetAsync($"api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(report.GetProperty("messages").GetArrayLength() >= provider.Messages.Count);

        provider.ThrowNext = true;
        var providerOutageOrder = await shopper.PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 4, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, providerOutageOrder.StatusCode,
            "A provider request failure must not fail order placement.");
        var outageBody = await providerOutageOrder.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("provider-error",
            outageBody.GetProperty("notifications")[0].GetProperty("providerStatus").GetString());
        var outageNotificationId = outageBody.GetProperty("notifications")[0].GetProperty("notificationId").GetInt32();
        var outageReport = await admin.GetFromJsonAsync<JsonElement>(
            $"api/notifications/reconciliation?from={from}&to={to}");
        Assert.IsTrue(outageReport.GetProperty("messages").EnumerateArray().Any(x =>
            x.GetProperty("notificationId").GetInt32() == outageNotificationId &&
            !x.GetProperty("inProvider").GetBoolean() && x.GetProperty("inApplication").GetBoolean()));

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"api/contact-numbers/{contactId}")).StatusCode);
        var sendCount = provider.SendCount;
        Assert.AreEqual(HttpStatusCode.Created, (await shopper.PostAsJsonAsync("api/orders",
            new { items = new[] { new { catalogItemId = 3, quantity = 1 } } })).StatusCode);
        Assert.AreEqual(sendCount, provider.SendCount, "A removed number must never be messaged again.");
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        private int _sequence;
        public ConcurrentDictionary<string, ProviderMessage> Messages { get; } = new();
        public bool FailNext { get; set; }
        public bool ThrowNext { get; set; }
        public int SendCount => _sequence;

        public Task<PhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, string? countryCode,
            CancellationToken cancellationToken) => Task.FromResult(new PhoneNumberLookup(true, "fake-destination"));

        public Task<ProviderMessage> SendMessageAsync(string to, string content, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new TwilioProviderException("test message creation");
            }
            var status = sendAt is not null ? "scheduled" : FailNext ? "undelivered" : "delivered";
            FailNext = false;
            var message = new ProviderMessage(sid, status, content, "fake-sender", to,
                status == "undelivered" ? 30007 : null, null, DateTimeOffset.UtcNow,
                sendAt is null ? DateTimeOffset.UtcNow : null, DateTimeOffset.UtcNow);
            Messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[messageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[messageSid] = Messages[messageSid] with
            { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow });

        public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[messageSid] = Messages[messageSid] with
            { Body = string.Empty, DateUpdated = DateTimeOffset.UtcNow });

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages.Values.ToList());
    }
}
