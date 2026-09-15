using System;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.PublicApi.Twilio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task FullApiFlowPreservesOrdersAndEnforcesSafetyRules()
    {
        await using var factory = new NotificationApiFactory();
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());
        var provider = factory.Services.GetRequiredService<FakeTwilio>();

        var invalidContact = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "invalid" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidContact.StatusCode);

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "canonicalize me" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadAsync(contactResponse);
        var contactNumberId = contact.GetProperty("contactNumberId").GetInt32();
        Assert.AreEqual(FakeTwilio.CanonicalNumber, contact.GetProperty("phoneNumber").GetString());

        var otherShopperDelete = await admin.DeleteAsync($"/api/contact-numbers/{contactNumberId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherShopperDelete.StatusCode);

        provider.ThrowOnNextCreate = true;
        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", OrderRequest());
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await ReadAsync(orderResponse)).GetProperty("orderId").GetInt32();

        var shopperDispatch = await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperDispatch.StatusCode);

        var notificationResponse = await shopper.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.OK, notificationResponse.StatusCode);
        var placedNotification = (await ReadAsync(notificationResponse))
            .GetProperty("notifications")[0];
        var placedNotificationId = placedNotification.GetProperty("notificationId").GetInt32();
        Assert.AreEqual("failed", placedNotification.GetProperty("providerStatus").GetString());

        var otherShopperOrder = await admin.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, otherShopperOrder.StatusCode);
        var shopperResend = await shopper.PostAsJsonAsync(
            $"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "not-authorized" });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperResend.StatusCode);
        var shopperRedact = await shopper.DeleteAsync($"/api/notifications/{placedNotificationId}/content");
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperRedact.StatusCode);
        var shopperReconciliation = await shopper.GetAsync(
            $"/api/notifications/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperReconciliation.StatusCode);

        var sendsBeforeResend = provider.CreateCount;
        var resendOne = await admin.PostAsJsonAsync(
            $"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "same-key" });
        var resendTwo = await admin.PostAsJsonAsync(
            $"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "same-key" });
        Assert.AreEqual(HttpStatusCode.OK, resendOne.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, resendTwo.StatusCode);
        Assert.AreEqual(
            (await ReadAsync(resendOne)).GetProperty("notificationId").GetInt32(),
            (await ReadAsync(resendTwo)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sendsBeforeResend + 1, provider.CreateCount);

        var freshResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{placedNotificationId}/resend",
            new { idempotencyKey = "fresh-key" });
        Assert.AreEqual(HttpStatusCode.OK, freshResend.StatusCode);
        Assert.AreNotEqual(
            (await ReadAsync(resendOne)).GetProperty("notificationId").GetInt32(),
            (await ReadAsync(freshResend)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sendsBeforeResend + 2, provider.CreateCount);

        var dispatch = await admin.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.IsTrue(provider.Messages.Any(x => x.Status == "scheduled"));

        var dispatchedNotifications = await ReadAsync(await shopper.GetAsync($"/api/orders/{orderId}/notifications"));
        var dispatchedNotification = dispatchedNotifications.GetProperty("notifications").EnumerateArray()
            .Single(x => x.GetProperty("kind").GetString() == "OrderDispatched");
        var dispatchedNotificationId = dispatchedNotification.GetProperty("notificationId").GetInt32();
        var dispatchedProviderSid = dispatchedNotification.GetProperty("providerMessageSid").GetString();

        var cancel = await admin.PostAsync($"/api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.IsFalse(provider.Messages.Any(x => x.Status == "scheduled"));
        Assert.IsTrue(provider.Messages.Any(x => x.Status == "canceled"));

        var redact = await admin.DeleteAsync($"/api/notifications/{dispatchedNotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, redact.StatusCode);
        var redactedNotifications = await ReadAsync(await shopper.GetAsync($"/api/orders/{orderId}/notifications"));
        var redacted = redactedNotifications.GetProperty("notifications").EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == dispatchedNotificationId);
        Assert.IsTrue(redacted.GetProperty("contentRedacted").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, redacted.GetProperty("content").ValueKind);
        Assert.AreEqual(string.Empty, provider.Messages.Single(x => x.Sid == dispatchedProviderSid).Body);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        Assert.IsTrue((await ReadAsync(reconciliation)).GetProperty("entries").GetArrayLength() > 0);
        Assert.AreEqual(1, provider.ListCount);

        var remove = await shopper.DeleteAsync($"/api/contact-numbers/{contactNumberId}");
        Assert.AreEqual(HttpStatusCode.NoContent, remove.StatusCode);
        var sendsBeforeOrderWithoutContact = provider.CreateCount;
        var secondOrder = await shopper.PostAsJsonAsync("/api/orders", OrderRequest());
        Assert.AreEqual(HttpStatusCode.Created, secondOrder.StatusCode);
        Assert.AreEqual(sendsBeforeOrderWithoutContact, provider.CreateCount);
    }

    private static object OrderRequest() => new
    {
        items = new[] { new { catalogItemId = 1, quantity = 1 } },
        shippingAddress = new
        {
            street = "1 Main Street",
            city = "Toronto",
            state = "ON",
            country = "Canada",
            zipCode = "M5V 1A1"
        }
    };

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}

internal sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TwilioRestClient>();
            services.RemoveAll<ITwilioLookupClient>();
            services.RemoveAll<ITwilioMessagingClient>();
            services.AddSingleton<FakeTwilio>();
            services.AddSingleton<ITwilioLookupClient>(x => x.GetRequiredService<FakeTwilio>());
            services.AddSingleton<ITwilioMessagingClient>(x => x.GetRequiredService<FakeTwilio>());
        });
    }
}

internal sealed class FakeTwilio : ITwilioLookupClient, ITwilioMessagingClient
{
    public const string CanonicalNumber = "+15550000000";
    private int _sequence;

    public List<TwilioMessage> Messages { get; } = new();
    public string NextStatus { get; set; } = "queued";
    public bool ThrowOnNextCreate { get; set; }
    public int CreateCount { get; private set; }
    public int ListCount { get; private set; }

    public Task<TwilioPhoneLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
        => Task.FromResult(new TwilioPhoneLookup(phoneNumber != "invalid", CanonicalNumber));

    public Task<TwilioMessage> CreateAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        CreateCount++;
        if (ThrowOnNextCreate)
        {
            ThrowOnNextCreate = false;
            throw new TwilioApiException(503, 20500, "Provider unavailable.");
        }

        var sid = "SM" + (++_sequence).ToString("x32");
        var status = sendAt == null ? NextStatus : "scheduled";
        NextStatus = "queued";
        var now = DateTimeOffset.UtcNow;
        var message = new TwilioMessage(sid, "+15551112222", to, status, body, null, now, sendAt == null ? now : null, now);
        Messages.Add(message);
        return Task.FromResult(message);
    }

    public Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken)
        => Task.FromResult(Messages.Single(x => x.Sid == messageSid));

    public Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
        => Task.FromResult(Update(messageSid, message => message with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow }));

    public Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken)
        => Task.FromResult(Update(messageSid, message => message with { Body = string.Empty, DateUpdated = DateTimeOffset.UtcNow }));

    public Task<IReadOnlyList<TwilioMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ListCount++;
        return Task.FromResult<IReadOnlyList<TwilioMessage>>(Messages
            .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
            .ToList());
    }

    private TwilioMessage Update(string sid, Func<TwilioMessage, TwilioMessage> update)
    {
        var index = Messages.FindIndex(x => x.Sid == sid);
        Messages[index] = update(Messages[index]);
        return Messages[index];
    }
}
