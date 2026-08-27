using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationFlowTest
{
    [TestMethod]
    public async Task FullFlowIsOwnedCancelableIdempotentRedactableAndReconcilable()
    {
        var provider = new FakeTwilioGateway();
        await using var factory = new NotificationApiFactory(provider);
        var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { mobileNumber = "+1 (555) 000-1111" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await contactResponse.Content.ReadFromJsonAsync<IdResponse>();
        Assert.IsTrue(contact!.ContactNumberId > 0);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shipToAddress = new { street = "1 Test St", city = "Toronto", state = "ON", country = "CA", zipCode = "M5V 1A1" }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<IdResponse>();
        Assert.IsTrue(order!.OrderId > 0);

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/orders/{order.OrderId}/notifications")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);

        (await admin.PostAsync($"/api/orders/{order.OrderId}/dispatch", null)).EnsureSuccessStatusCode();
        var afterDispatch = await shopper.GetFromJsonAsync<NotificationView[]>(
            $"/api/orders/{order.OrderId}/notifications");
        var failedPlaced = afterDispatch!.Single(item => item.Kind == "OrderPlaced");
        var followUp = afterDispatch!.Single(item => item.Kind == "DeliveryFollowUp");
        Assert.AreEqual("undelivered", failedPlaced.ProviderStatus);
        Assert.AreEqual("scheduled", followUp.ProviderStatus);

        var firstResend = await admin.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new { idempotencyKey = "attempt-one" });
        firstResend.EnsureSuccessStatusCode();
        var firstResendId = (await firstResend.Content.ReadFromJsonAsync<IdResponse>())!.NotificationId;
        var duplicateResend = await admin.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new { idempotencyKey = "attempt-one" });
        Assert.AreEqual(firstResendId,
            (await duplicateResend.Content.ReadFromJsonAsync<IdResponse>())!.NotificationId);
        Assert.AreEqual(2, provider.ResendBodyCount);

        var secondResend = await admin.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new { idempotencyKey = "attempt-two" });
        secondResend.EnsureSuccessStatusCode();
        Assert.AreNotEqual(firstResendId,
            (await secondResend.Content.ReadFromJsonAsync<IdResponse>())!.NotificationId);
        Assert.AreEqual(3, provider.ResendBodyCount);

        (await admin.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.AreEqual("canceled", provider.Messages[followUp.ProviderMessageSid!].Status);

        var afterCancel = await shopper.GetFromJsonAsync<NotificationView[]>(
            $"/api/orders/{order.OrderId}/notifications");
        var cancellation = afterCancel!.Single(item => item.Kind == "OrderCancelled");
        var redact = await admin.DeleteAsync($"/api/notifications/{cancellation.NotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, redact.StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages[cancellation.ProviderMessageSid!].Body);
        var afterRedaction = await shopper.GetFromJsonAsync<NotificationView[]>(
            $"/api/orders/{order.OrderId}/notifications");
        Assert.IsTrue(afterRedaction!.Single(item => item.NotificationId == cancellation.NotificationId).ContentDisposed);

        var now = DateTimeOffset.UtcNow;
        var report = await admin.GetAsync($"/api/notifications/reconciliation?from={Uri.EscapeDataString(now.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(1).ToString("O"))}");
        report.EnsureSuccessStatusCode();
        var reconciliation = await report.Content.ReadFromJsonAsync<ReconciliationView>();
        Assert.IsTrue(reconciliation!.Count >= 6);
        Assert.IsTrue(reconciliation.Entries.Any(entry => entry.Match == "matched"));

        var delete = await shopper.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.AreEqual(0, (await shopper.GetFromJsonAsync<IdResponse[]>("/api/contact-numbers"))!.Length);
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        private readonly ITwilioGateway _provider;
        public NotificationApiFactory(ITwilioGateway provider) => _provider = provider;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton(_provider);
            });
        }
    }

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        private int _sequence;
        public ConcurrentDictionary<string, MutableMessage> Messages { get; } = new();
        public int ResendBodyCount => Messages.Values.Count(message => message.Body.Contains("has been placed"));

        public Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidationResult(true, "+15550001111"));

        public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var sid = "SM" + Interlocked.Increment(ref _sequence).ToString().PadLeft(32, '0');
            var status = sendAt.HasValue ? "scheduled" : body.Contains("has been placed") ? "undelivered" : "delivered";
            var message = new MutableMessage(sid, to, body, status, DateTimeOffset.UtcNow, sendAt);
            Messages[sid] = message;
            return Task.FromResult(message.Snapshot());
        }

        public Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[providerMessageSid].Snapshot());

        public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            Messages[providerMessageSid].Status = "canceled";
            return Task.FromResult(Messages[providerMessageSid].Snapshot());
        }

        public Task<ProviderMessage> RedactMessageContentAsync(string providerMessageSid,
            CancellationToken cancellationToken)
        {
            Messages[providerMessageSid].Body = string.Empty;
            return Task.FromResult(Messages[providerMessageSid].Snapshot());
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderMessage>>(
            Messages.Values.Where(message => message.CreatedAt >= from && message.CreatedAt <= to)
                .Select(message => message.Snapshot()).ToArray());
    }

    public sealed class MutableMessage
    {
        public MutableMessage(string sid, string to, string body, string status, DateTimeOffset createdAt,
            DateTimeOffset? scheduledFor)
        {
            Sid = sid; To = to; Body = body; Status = status; CreatedAt = createdAt; ScheduledFor = scheduledFor;
        }
        public string Sid { get; }
        public string To { get; }
        public string Body { get; set; }
        public string Status { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ScheduledFor { get; }
        public ProviderMessage Snapshot() => new(Sid, "+15550002222", To, Status, Body, CreatedAt,
            Status == "scheduled" ? null : CreatedAt, DateTimeOffset.UtcNow,
            Status == "undelivered" ? 30034 : null);
    }

    private sealed class IdResponse
    {
        public int ContactNumberId { get; set; }
        public int OrderId { get; set; }
        public int NotificationId { get; set; }
    }
    private sealed class NotificationView
    {
        public int NotificationId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string ProviderStatus { get; set; } = string.Empty;
        public string? ProviderMessageSid { get; set; }
        public bool ContentDisposed { get; set; }
    }
    private sealed class ReconciliationView
    {
        public int Count { get; set; }
        public ReconciliationEntryView[] Entries { get; set; } = Array.Empty<ReconciliationEntryView>();
    }
    private sealed class ReconciliationEntryView { public string Match { get; set; } = string.Empty; }
}
