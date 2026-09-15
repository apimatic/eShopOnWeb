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
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    private NotificationApiFactory _factory = null!;

    [TestInitialize]
    public void Initialize() => _factory = new NotificationApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task ShopperAndOperatorCanDriveOrderLifecycleAndCancelScheduledFollowUp()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { number = "valid" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = (await Json(contactResponse)).GetProperty("contactNumberId").GetInt32();

        var otherUsersContacts = await admin.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, otherUsersContacts.GetProperty("contactNumbers").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new { street = "1 Test St", city = "Toronto", state = "ON", country = "Canada", zipCode = "A1A 1A1" }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await Json(orderResponse)).GetProperty("orderId").GetInt32();

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        _factory.Provider.CancelFailuresRemaining = 1;
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);

        var notifications = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var rows = notifications.GetProperty("notifications").EnumerateArray().ToList();
        Assert.AreEqual(4, rows.Count);
        Assert.AreEqual("canceled", rows.Single(x => x.GetProperty("type").GetString() == "DeliveryFollowUp").GetProperty("status").GetString());
        Assert.IsTrue(rows.All(x => x.TryGetProperty("notificationId", out _)));
    }

    [TestMethod]
    public async Task FailedMessageResendIsIdempotentAndProviderContentIsRedacted()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        await shopper.PostAsJsonAsync("/api/contact-numbers", new { number = "unreachable" });
        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } },
            shippingAddress = new { street = "2 Test St", city = "Toronto", state = "ON", country = "Canada", zipCode = "A1A 1A1" }
        });
        var orderId = (await Json(orderResponse)).GetProperty("orderId").GetInt32();
        var notifications = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var failedId = notifications.GetProperty("notifications").EnumerateArray()
            .First(x => x.GetProperty("status").GetString() == "undelivered").GetProperty("notificationId").GetInt32();

        var before = _factory.Provider.SendCount;
        var first = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "same-key" });
        var second = await admin.PostAsJsonAsync($"/api/notifications/{failedId}/resend", new { idempotencyKey = "same-key" });
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual((await Json(first)).GetProperty("notificationId").GetInt32(), (await Json(second)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(before + 1, _factory.Provider.SendCount);

        var dispose = await admin.DeleteAsync($"/api/notifications/{failedId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, dispose.StatusCode);
        notifications = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var disposed = notifications.GetProperty("notifications").EnumerateArray().Single(x => x.GetProperty("notificationId").GetInt32() == failedId);
        Assert.AreEqual(JsonValueKind.Null, disposed.GetProperty("content").ValueKind);
        Assert.IsNull(_factory.Provider.Messages[disposed.GetProperty("providerMessageSid").GetString()!].Body);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

public sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeTwilioGateway Provider { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITwilioMessagingGateway>();
            services.AddSingleton<ITwilioMessagingGateway>(Provider);
        });
    }
}

public sealed class FakeTwilioGateway : ITwilioMessagingGateway
{
    private int _sequence;
    public int SendCount => _sequence;
    public int CancelFailuresRemaining { get; set; }
    public ConcurrentDictionary<string, ProviderMessage> Messages { get; } = new();

    public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(rawNumber == "valid"
            ? new PhoneNumberValidation(true, "+14165550100", Array.Empty<string>())
            : new PhoneNumberValidation(true, "+12025550199", Array.Empty<string>()));

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
        var status = sendAt.HasValue ? "scheduled" : to == "+12025550199" ? "undelivered" : "delivered";
        var message = new ProviderMessage(sid, status, "+14165550000", to, body,
            status == "undelivered" ? 30005 : null, DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
        Messages[sid] = message;
        return Task.FromResult(message);
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default) => Task.FromResult(Messages[messageSid]);
    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        if (CancelFailuresRemaining-- > 0) throw new TwilioProviderException("Simulated provider failure.");
        return Task.FromResult(Messages.AddOrUpdate(messageSid, _ => throw new KeyNotFoundException(), (_, x) => x with { Status = "canceled" }));
    }
    public Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken = default) =>
        Task.FromResult(Messages.AddOrUpdate(messageSid, _ => throw new KeyNotFoundException(), (_, x) => x with { Body = null }));
    public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages.Values.Where(x => x.DateSent >= from && x.DateSent <= to).ToList());
}
