using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationFlowTests
{
    [TestMethod]
    public async Task ProviderFailureDoesNotFailOrderPlacement()
    {
        var provider = new FakeSmsProvider { ThrowOnSend = true };
        await using var application = new NotificationApiFactory(provider);
        using var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        Assert.AreEqual(HttpStatusCode.Created, (await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { phoneNumber = "+14165550199" })).StatusCode);
        var place = await shopper.PostAsJsonAsync("/api/orders",
            new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.Created, place.StatusCode);
        var orderId = (await place.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        var notifications = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        Assert.AreEqual("provider-request-failed",
            notifications.GetProperty("notifications")[0].GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task FullApiFlowHonorsOwnershipLifecycleIdempotencyAndReconciliation()
    {
        var provider = new FakeSmsProvider();
        await using var application = new NotificationApiFactory(provider);
        using var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        using var admin = application.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var register = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "+14165550199" });
        Assert.AreEqual(HttpStatusCode.Created, register.StatusCode);
        var contactNumberId = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contactNumberId").GetInt32();

        var adminContacts = await admin.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, adminContacts.GetProperty("contactNumbers").GetArrayLength(), "Contacts must be shopper-owned.");

        var place = await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, place.StatusCode);
        var orderId = (await place.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();

        var forbiddenDispatch = await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);

        var beforeCancel = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var notifications = beforeCancel.GetProperty("notifications").EnumerateArray().ToArray();
        var failedId = notifications.Single(x => x.GetProperty("kind").GetString() == "OrderPlaced")
            .GetProperty("notificationId").GetInt32();
        Assert.IsTrue(notifications.Any(x => x.GetProperty("status").GetString() == "scheduled"));

        var sendsBeforeResend = provider.ImmediateSendCount;
        var resend1 = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "retry-one" });
        Assert.AreEqual(HttpStatusCode.Created, resend1.StatusCode);
        var resentId = (await resend1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetInt32();
        var resend2 = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "retry-one" });
        Assert.AreEqual(HttpStatusCode.OK, resend2.StatusCode);
        Assert.AreEqual(resentId, (await resend2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sendsBeforeResend + 1, provider.ImmediateSendCount, "An idempotency replay must not send again.");

        Assert.AreEqual(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/notifications/{resentId}/content")).StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages.Values.Single(x => x.LocalNotificationOrdinal == provider.LastOrdinal).Body);

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        var afterCancel = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        Assert.IsTrue(afterCancel.GetProperty("notifications").EnumerateArray()
            .Any(x => x.GetProperty("kind").GetString() == "DeliveryFollowUp" && x.GetProperty("status").GetString() == "canceled"));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        var report = await reconciliation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(report.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("match").GetString() == "matched"));

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);
        var sendsAfterDelete = provider.ImmediateSendCount;
        var secondOrder = await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, secondOrder.StatusCode, "No-number shoppers still place orders successfully.");
        Assert.AreEqual(sendsAfterDelete, provider.ImmediateSendCount, "Removed numbers must never be messaged again.");
    }

    private sealed class NotificationApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakeSmsProvider _provider;
        private readonly string _databaseId = Guid.NewGuid().ToString("N");
        public NotificationApiFactory(FakeSmsProvider provider) => _provider = provider;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
                services.AddScoped(serviceProvider => new DbContextOptionsBuilder<CatalogContext>()
                    .UseInMemoryDatabase("Catalog-" + _databaseId)
                    .UseApplicationServiceProvider(serviceProvider).Options);
                services.AddScoped(serviceProvider => new DbContextOptionsBuilder<AppIdentityDbContext>()
                    .UseInMemoryDatabase("Identity-" + _databaseId)
                    .UseApplicationServiceProvider(serviceProvider).Options);
                services.AddSingleton<ISmsProvider>(_provider);
            });
        }
    }

    private sealed class FakeSmsProvider : ISmsProvider
    {
        private int _ordinal;
        public ConcurrentDictionary<string, FakeMessage> Messages { get; } = new();
        public int ImmediateSendCount { get; private set; }
        public int LastOrdinal => _ordinal;
        public bool ThrowOnSend { get; init; }

        public Task<PhoneNumberLookupResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberLookupResult(true, phoneNumber, Array.Empty<string>()));

        public Task<SmsProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken)
        {
            if (ThrowOnSend) throw new SmsProviderException("Simulated provider outage.");
            ImmediateSendCount++;
            return Task.FromResult(Create(to, body, "undelivered", DateTimeOffset.UtcNow));
        }

        public Task<SmsProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
            Task.FromResult(Create(to, body, "scheduled", null));

        public Task<SmsProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(ToProvider(Messages[messageSid]));

        public Task<SmsProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
        {
            Messages[messageSid].Status = "canceled";
            return GetAsync(messageSid, cancellationToken);
        }

        public Task<SmsProviderMessage> DisposeContentAsync(string messageSid, CancellationToken cancellationToken)
        {
            Messages[messageSid].Body = string.Empty;
            return GetAsync(messageSid, cancellationToken);
        }

        public Task<IReadOnlyList<SmsProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SmsProviderMessage>>(Messages.Values.Where(x => x.CreatedAt >= from && x.CreatedAt < to)
                .Select(ToProvider).ToList());

        private SmsProviderMessage Create(string to, string body, string status, DateTimeOffset? sentAt)
        {
            var ordinal = Interlocked.Increment(ref _ordinal);
            var sid = $"SM{ordinal:D32}";
            var message = new FakeMessage(ordinal, sid, status, body, to, DateTimeOffset.UtcNow, sentAt);
            Messages[sid] = message;
            return ToProvider(message);
        }

        private static SmsProviderMessage ToProvider(FakeMessage x) => new(x.Sid, x.Status, x.Body,
            "+15551234567", x.To, x.Status == "undelivered" ? 30007 : null, x.CreatedAt, x.SentAt);
    }

    private sealed class FakeMessage
    {
        public FakeMessage(int ordinal, string sid, string status, string body, string to, DateTimeOffset createdAt, DateTimeOffset? sentAt)
        {
            LocalNotificationOrdinal = ordinal;
            Sid = sid;
            Status = status;
            Body = body;
            To = to;
            CreatedAt = createdAt;
            SentAt = sentAt;
        }
        public int LocalNotificationOrdinal { get; }
        public string Sid { get; }
        public string Status { get; set; }
        public string Body { get; set; }
        public string To { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? SentAt { get; }
    }
}
