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
public class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task ProviderFailureDoesNotFailOrderPlacement()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton<ISmsProvider>(new FailingSmsProvider());
            });
        });

        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetUserToken("provider-failure@example.test"));
        var contact = await shopper.PostAsJsonAsync("/api/contact-numbers", new { number = "provider input" });
        contact.EnsureSuccessStatusCode();

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });

        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderCreated>();
        Assert.IsNotNull(order);
        var notifications = await shopper.GetFromJsonAsync<List<NotificationResult>>(
            $"/api/orders/{order.OrderId}/notifications");
        Assert.IsNotNull(notifications);
        Assert.AreEqual("provider-request-failed", notifications.Single().DeliveryStatus);
    }

    [TestMethod]
    public async Task DrivesShopperAndOperatorFlowWithAuthorizationAndIdempotency()
    {
        var provider = new RecordingSmsProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton<ISmsProvider>(provider);
            });
        });

        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetUserToken("notification-flow@example.test"));
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { number = "provider input" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await contactResponse.Content.ReadFromJsonAsync<ContactCreated>();
        Assert.IsNotNull(contact);
        Assert.AreEqual(RecordingSmsProvider.CanonicalNumber, contact.Number);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderCreated>();
        Assert.IsNotNull(order);

        var forbiddenDispatch = await shopper.PostAsync($"/api/orders/{order.OrderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);

        var dispatch = await admin.PostAsync($"/api/orders/{order.OrderId}/dispatch", null);
        dispatch.EnsureSuccessStatusCode();

        var notifications = await shopper.GetFromJsonAsync<List<NotificationResult>>(
            $"/api/orders/{order.OrderId}/notifications");
        Assert.IsNotNull(notifications);
        Assert.AreEqual(3, notifications.Count);
        var placed = notifications.Single(x => x.Type == "OrderPlaced");
        var followUp = notifications.Single(x => x.Type == "DeliveryFollowUp");
        Assert.AreEqual("undelivered", placed.DeliveryStatus);
        Assert.AreEqual("scheduled", followUp.DeliveryStatus);

        var firstResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend",
            new { idempotencyKey = "same-attempt" });
        firstResend.EnsureSuccessStatusCode();
        var firstResendBody = await firstResend.Content.ReadFromJsonAsync<ResendCreated>();
        Assert.IsNotNull(firstResendBody);

        var repeatedResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend",
            new { idempotencyKey = "same-attempt" });
        var repeatedResendBody = await repeatedResend.Content.ReadFromJsonAsync<ResendCreated>();
        Assert.IsNotNull(repeatedResendBody);
        Assert.AreEqual(firstResendBody.NotificationId, repeatedResendBody.NotificationId);

        var cancel = await admin.PostAsync($"/api/orders/{order.OrderId}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        Assert.AreEqual("canceled", provider.Messages[followUp.ProviderMessageSid!].Status);

        var dispose = await admin.DeleteAsync($"/api/notifications/{placed.NotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages[placed.ProviderMessageSid!].Body);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        reconciliation.EnsureSuccessStatusCode();
        var report = await reconciliation.Content.ReadFromJsonAsync<ReconciliationResult>();
        Assert.IsNotNull(report);
        Assert.IsTrue(report.MatchedCount >= 1);

        var delete = await shopper.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var contacts = await shopper.GetFromJsonAsync<List<ContactCreated>>("/api/contact-numbers");
        Assert.IsNotNull(contacts);
        Assert.AreEqual(0, contacts.Count);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed record ContactCreated(int ContactNumberId, string Number);
    private sealed record OrderCreated(int OrderId);
    private sealed record ResendCreated(int NotificationId);
    private sealed record ReconciliationResult(int MatchedCount);
    private sealed record NotificationResult(
        int NotificationId,
        string Type,
        string DeliveryStatus,
        string? ProviderMessageSid);

    private sealed class RecordingSmsProvider : ISmsProvider
    {
        public const string CanonicalNumber = "+15550000001";
        private int _nextSid;
        public ConcurrentDictionary<string, SmsProviderMessage> Messages { get; } = new();

        public Task<SmsDestinationValidation> ValidateDestinationAsync(string number, CancellationToken cancellationToken) =>
            Task.FromResult(new SmsDestinationValidation(true, CanonicalNumber));

        public Task<SmsProviderMessage> SendAsync(
            string to,
            string body,
            DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var sid = $"SM{Interlocked.Increment(ref _nextSid):D32}";
            var now = DateTimeOffset.UtcNow;
            var status = sendAt is not null ? "scheduled" : body.Contains("has been placed", StringComparison.Ordinal) ? "undelivered" : "queued";
            var message = new SmsProviderMessage(sid, status, "+15550000002", to, body, now, sendAt is null ? now : null, now, null);
            Messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<SmsProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[providerMessageSid]);

        public Task<SmsProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var current = Messages[providerMessageSid];
            var updated = current with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow };
            Messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<SmsProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var current = Messages[providerMessageSid];
            var updated = current with { Body = string.Empty, DateUpdated = DateTimeOffset.UtcNow };
            Messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<SmsProviderMessage>> ListMessagesAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SmsProviderMessage>>(Messages.Values
                .Where(x => x.DateSent >= from && x.DateSent <= to)
                .ToList());
    }

    private sealed class FailingSmsProvider : ISmsProvider
    {
        public Task<SmsDestinationValidation> ValidateDestinationAsync(string number, CancellationToken cancellationToken) =>
            Task.FromResult(new SmsDestinationValidation(true, "+15550000003"));

        public Task<SmsProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken) =>
            throw new SmsProviderException("test send", 30001);

        public Task<SmsProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SmsProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SmsProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SmsProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SmsProviderMessage>>(Array.Empty<SmsProviderMessage>());
    }
}
