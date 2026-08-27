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
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task DrivesOrderLifecycleAndKeepsContactShopperScoped()
    {
        using var fixture = new NotificationApiFactory();
        using var shopper = fixture.CreateClient();
        Authorize(shopper, ApiTokenHelper.GetNormalUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "reachable-test-destination" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = await ReadIntAsync(contactResponse, "contactNumberId");

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = await ReadIntAsync(orderResponse, "orderId");

        using var otherShopper = fixture.CreateClient();
        Authorize(otherShopper, ApiTokenHelper.GetAdminUserToken());
        var otherContacts = await otherShopper.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, otherContacts.GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound, (await otherShopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);

        Assert.IsTrue((await otherShopper.PostAsync($"/api/orders/{orderId}/dispatch", null)).IsSuccessStatusCode);
        var dispatched = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(3, dispatched.GetArrayLength());
        var followUp = dispatched.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "deliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.GetProperty("providerStatus").GetString());

        Assert.IsTrue((await otherShopper.PostAsync($"/api/orders/{orderId}/cancel", null)).IsSuccessStatusCode);
        var cancelled = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        followUp = cancelled.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "deliveryFollowUp");
        Assert.AreEqual("canceled", followUp.GetProperty("providerStatus").GetString());

        var placed = cancelled.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "orderPlaced");
        var notificationId = placed.GetProperty("notificationId").GetInt32();
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await otherShopper.DeleteAsync($"/api/notifications/{notificationId}/content")).StatusCode);
        var redacted = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        placed = redacted.EnumerateArray().Single(x => x.GetProperty("notificationId").GetInt32() == notificationId);
        Assert.IsTrue(placed.GetProperty("contentRedacted").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, placed.GetProperty("content").ValueKind);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        var report = await otherShopper.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        report.EnsureSuccessStatusCode();
        var reportJson = await report.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(reportJson.GetProperty("providerMessages").GetArrayLength() >= 3);
    }

    [TestMethod]
    public async Task ResendUsesCallerKeyAtMostOnceAndFreshKeyCreatesAnotherAttempt()
    {
        using var fixture = new NotificationApiFactory();
        using var shopper = fixture.CreateClient();
        Authorize(shopper, ApiTokenHelper.GetNormalUserToken());
        (await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "unreachable-test-destination" })).EnsureSuccessStatusCode();
        var order = await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = await ReadIntAsync(order, "orderId");
        var notifications = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var failedId = notifications[0].GetProperty("notificationId").GetInt32();

        using var admin = fixture.CreateClient();
        Authorize(admin, ApiTokenHelper.GetAdminUserToken());
        var first = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-one" });
        var firstId = await ReadIntAsync(first, "notificationId");
        var repeated = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-one" });
        Assert.AreEqual(firstId, await ReadIntAsync(repeated, "notificationId"));
        Assert.AreEqual(2, fixture.Provider.SendCount);

        var fresh = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "attempt-two" });
        Assert.AreNotEqual(firstId, await ReadIntAsync(fresh, "notificationId"));
        Assert.AreEqual(3, fixture.Provider.SendCount);
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<int> ReadIntAsync(HttpResponseMessage response, string property)
    {
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty(property).GetInt32();
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        public FakeSmsProvider Provider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var databaseName = $"Notifications-{Guid.NewGuid()}";
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton<ISmsProvider>(Provider);
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.RemoveAll<CatalogContext>();
                services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }
    }

    private sealed class FakeSmsProvider : ISmsProvider
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private int _next;
        public int SendCount => _next;

        public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidation(true, phoneNumber));

        public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            var sid = $"SM{Interlocked.Increment(ref _next):D32}";
            var status = sendAt is not null ? "scheduled" : to == "unreachable-test-destination" ? "undelivered" : "delivered";
            var message = new ProviderMessage(sid, status, null, to, body,
                status == "undelivered" ? 30003 : null, DateTimeOffset.UtcNow, sendAt is null ? DateTimeOffset.UtcNow : null);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Update(messageSid, x => x with { Status = "canceled" }));

        public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Update(messageSid, x => x with { Body = string.Empty }));

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.Where(x =>
                (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to).ToList());

        private ProviderMessage Update(string sid, Func<ProviderMessage, ProviderMessage> update) =>
            _messages.AddOrUpdate(sid, _ => throw new InvalidOperationException(), (_, current) => update(current));
    }
}
