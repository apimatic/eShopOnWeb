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
public class OrderNotificationFlowTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private FakeSmsProvider _provider = null!;
    private HttpClient _shopper = null!;
    private HttpClient _operator = null!;

    [TestInitialize]
    public void Initialize()
    {
        _provider = new FakeSmsProvider();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton<ISmsProvider>(_provider);
            });
        });

        _shopper = _factory.CreateClient();
        _shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        _operator = _factory.CreateClient();
        _operator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _shopper.Dispose();
        _operator.Dispose();
        await _factory.DisposeAsync();
    }

    [TestMethod]
    public async Task EntireFlowIsApiDrivableScopedAndIdempotent()
    {
        _provider.NextImmediateStatus = "undelivered";
        var contactResponse = await _shopper.PostAsJsonAsync("/api/contact-numbers", new
        {
            phoneNumber = "provider-fixture"
        });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = (await ReadJson(contactResponse)).GetProperty("contactNumberId").GetInt32();

        var orderResponse = await _shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await ReadJson(orderResponse)).GetProperty("orderId").GetInt32();

        var forbiddenDispatch = await _shopper.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);

        var hiddenFromAnotherUser = await _operator.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenFromAnotherUser.StatusCode);

        _provider.NextImmediateStatus = "queued";
        (await _operator.PostAsync($"/api/orders/{orderId}/dispatch", null)).EnsureSuccessStatusCode();

        var afterDispatch = await _shopper.GetAsync($"/api/orders/{orderId}/notifications");
        afterDispatch.EnsureSuccessStatusCode();
        var dispatchNotifications = await ReadJson(afterDispatch);
        Assert.AreEqual(3, dispatchNotifications.GetArrayLength());
        var scheduled = dispatchNotifications.EnumerateArray().Single(item =>
            item.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", scheduled.GetProperty("status").GetString());

        (await _operator.PostAsync($"/api/orders/{orderId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.AreEqual("canceled", _provider.GetMessage(scheduled.GetProperty("providerMessageSid").GetString()!).Status);

        var failedNotificationId = dispatchNotifications.EnumerateArray().Single(item =>
            item.GetProperty("status").GetString() == "undelivered").GetProperty("notificationId").GetInt32();
        var sendsBeforeResend = _provider.SendCount;
        var resendOne = await _operator.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "attempt-one"
        });
        Assert.AreEqual(HttpStatusCode.Created, resendOne.StatusCode);
        var resentId = (await ReadJson(resendOne)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(sendsBeforeResend + 1, _provider.SendCount);

        var resendDuplicate = await _operator.PostAsJsonAsync($"/api/notifications/{failedNotificationId}/resend", new
        {
            idempotencyKey = "attempt-one"
        });
        Assert.AreEqual(resentId, (await ReadJson(resendDuplicate)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(sendsBeforeResend + 1, _provider.SendCount);

        var redact = await _operator.DeleteAsync($"/api/notifications/{resentId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, redact.StatusCode);
        var afterRedaction = await _shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var redacted = afterRedaction.EnumerateArray().Single(item => item.GetProperty("notificationId").GetInt32() == resentId);
        Assert.AreEqual(JsonValueKind.Null, redacted.GetProperty("content").ValueKind);

        var rangeFrom = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        var rangeTo = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        var reconciliation = await _operator.GetFromJsonAsync<JsonElement>(
            $"/api/notifications/reconciliation?from={rangeFrom}&to={rangeTo}");
        Assert.IsTrue(reconciliation.GetProperty("messages").EnumerateArray().Any(item =>
            item.GetProperty("match").GetString() == "matched"));

        var deleteContact = await _shopper.DeleteAsync($"/api/contact-numbers/{contactId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleteContact.StatusCode);
        var contacts = await _shopper.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, contacts.GetArrayLength());

        var orders = await _shopper.GetFromJsonAsync<JsonElement>("/api/my-orders");
        Assert.AreEqual("Cancelled", orders[0].GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ProviderSendFailureDoesNotFailOrderPlacement()
    {
        (await _shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "provider-fixture" }))
            .EnsureSuccessStatusCode();
        _provider.ThrowOnSend = true;

        var response = await _shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var orderId = (await ReadJson(response)).GetProperty("orderId").GetInt32();
        var notifications = await _shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        Assert.AreEqual("provider-request-failed", notifications[0].GetProperty("status").GetString());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonDocument.ParseAsync(stream)).RootElement.Clone();
    }

    private sealed class FakeSmsProvider : ISmsProvider
    {
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        private int _sequence;

        public string NextImmediateStatus { get; set; } = "queued";
        public bool ThrowOnSend { get; set; }
        public int SendCount { get; private set; }

        public Task<PhoneNumberValidation> ValidateDestinationAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidation(true, "+10000000000", Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            SendCount++;
            if (ThrowOnSend) throw new SmsProviderException("test send");
            var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
            var status = sendAt is null ? NextImmediateStatus : "scheduled";
            var now = DateTimeOffset.UtcNow;
            var message = new ProviderMessage(sid, status, status == "undelivered" ? 30000 : null, now, sendAt is null ? now : null, body);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var existing = _messages[providerMessageSid];
            var canceled = existing with { Status = "canceled" };
            _messages[providerMessageSid] = canceled;
            return Task.FromResult(canceled);
        }

        public Task<ProviderMessage> RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var existing = _messages[providerMessageSid];
            var redacted = existing with { Body = string.Empty };
            _messages[providerMessageSid] = redacted;
            return Task.FromResult(redacted);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.Where(message =>
                (message.SentAt ?? message.CreatedAt) >= from && (message.SentAt ?? message.CreatedAt) <= to).ToList());

        public ProviderMessage GetMessage(string sid) => _messages[sid];
    }
}
