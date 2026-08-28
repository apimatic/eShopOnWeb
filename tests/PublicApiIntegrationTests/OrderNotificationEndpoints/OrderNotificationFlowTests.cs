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

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task FullFlowEnforcesScopesAndProviderLifecycle()
    {
        var gateway = new FakeTwilioGateway();
        await using var factory = new NotificationApiFactory(gateway);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { phoneNumber = "not-canonical" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = (await JsonDocument.ParseAsync(await contactResponse.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("contactNumberId").GetInt32();

        var adminContacts = await admin.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(0, adminContacts!.Length, "A different user must not see the shopper's contact.");
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            shipToAddress = new { street = "1 Main", city = "Toronto", state = "ON", country = "CA", zipCode = "A1A1A1" },
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await JsonDocument.ParseAsync(await orderResponse.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("orderId").GetInt32();

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);

        var beforeCancel = await shopper.GetFromJsonAsync<JsonElement[]>(
            $"/api/orders/{orderId}/notifications");
        Assert.AreEqual(3, beforeCancel!.Length);
        var placed = beforeCancel.Single(x => x.GetProperty("kind").GetString() == "OrderPlaced");
        var dispatched = beforeCancel.Single(x => x.GetProperty("kind").GetString() == "OrderDispatched");
        var followUp = beforeCancel.Single(x => x.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.IsTrue(followUp.GetProperty("scheduledFor").ValueKind == JsonValueKind.String);

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode,
            "An administrator is still a different shopper for shopper-scoped reads.");

        var placedId = placed.GetProperty("notificationId").GetInt32();
        var firstResend = await admin.PostAsJsonAsync($"/api/notifications/{placedId}/resend",
            new { idempotencyKey = "retry-1" });
        Assert.AreEqual(HttpStatusCode.Created, firstResend.StatusCode);
        var resendId = (await firstResend.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("notificationId").GetInt32();
        var repeatedResend = await admin.PostAsJsonAsync($"/api/notifications/{placedId}/resend",
            new { idempotencyKey = "retry-1" });
        Assert.AreEqual(resendId, (await repeatedResend.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("notificationId").GetInt32());
        Assert.AreEqual(4, gateway.CreateCount, "The repeated idempotency key must not create another message.");

        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual("canceled", gateway.Get(followUp.GetProperty("providerMessageSid").GetString()!).Status);
        Assert.IsTrue(gateway.CancelAttempts >= 2, "A transient provider cancellation failure must be retried.");
        Assert.AreEqual(5, gateway.CreateCount);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode,
            "Cancellation can be repeated to repair a transient provider cancellation failure.");
        Assert.AreEqual(5, gateway.CreateCount, "Repeating cancellation must not duplicate its shopper message.");

        var dispatchedId = dispatched.GetProperty("notificationId").GetInt32();
        var dispatchedSid = dispatched.GetProperty("providerMessageSid").GetString()!;
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{dispatchedId}/content")).StatusCode);
        Assert.IsNull(gateway.Get(dispatchedSid).Body, "Disposal must redact the provider's copy.");

        var now = DateTimeOffset.UtcNow;
        var report = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/notifications/reconciliation?from={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(now.AddDays(1).ToString("O"))}");
        Assert.IsTrue(report.GetProperty("entries").GetArrayLength() >= 5);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        var contactsAfterDelete = await shopper.GetFromJsonAsync<JsonElement[]>("/api/contact-numbers");
        Assert.AreEqual(0, contactsAfterDelete!.Length);
        var orderAfterDelete = await shopper.PostAsJsonAsync("/api/orders", new
        {
            shipToAddress = new { street = "1 Main", city = "Toronto", state = "ON", country = "CA", zipCode = "A1A1A1" },
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderAfterDelete.StatusCode);
        Assert.AreEqual(5, gateway.CreateCount, "No later operation may message a deleted contact.");
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        private readonly ITwilioGateway _gateway;
        public NotificationApiFactory(ITwilioGateway gateway) => _gateway = gateway;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton(_gateway);
            });
        }
    }

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private int _sequence;
        private int _cancelAttempts;
        public int CreateCount => _sequence;
        public int CancelAttempts => _cancelAttempts;
        public ProviderMessage Get(string sid) => _messages[sid];

        public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber,
            CancellationToken cancellationToken) => Task.FromResult(new PhoneNumberValidation(true, "canonical-destination"));

        public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref _sequence);
            var sid = "SM" + number.ToString().PadLeft(32, '0');
            var status = sendAt.HasValue ? "scheduled" : number == 1 ? "undelivered" : "delivered";
            var message = new ProviderMessage(sid, body, "configured-sender", to, status,
                status == "undelivered" ? 30003 : null, DateTimeOffset.UtcNow,
                sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _cancelAttempts) == 1)
                throw new TwilioGatewayException(20404);
            var current = _messages[messageSid];
            var updated = current with { Status = "canceled" };
            _messages[messageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = _messages[messageSid];
            var updated = current with { Body = null };
            _messages[messageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderMessage>>(
            _messages.Values.Where(x => (x.DateSent ?? x.DateCreated) >= from &&
                (x.DateSent ?? x.DateCreated) <= to).ToList());
    }
}
