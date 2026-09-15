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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public async Task CompleteFlowIsScopedAuthorizedAndIdempotent()
    {
        var provider = new FakeProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPhoneNumberValidator>();
                services.RemoveAll<IMessageProvider>();
                services.AddSingleton<IPhoneNumberValidator>(provider);
                services.AddSingleton<IMessageProvider>(provider);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var contactResponse = await client.PostAsJsonAsync("/api/contact-numbers", new { number = "typed number" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadAsync<ContactNumberCreatedResponse>(contactResponse);
        Assert.IsTrue(contact.ContactNumberId > 0);

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await ReadAsync<OrderCreatedResponse>(orderResponse);
        Assert.IsTrue(order.OrderId > 0);

        var forbiddenDispatch = await client.PostAsync($"/api/orders/{order.OrderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);

        SetToken(client, ApiTokenHelper.GetOtherUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/orders/{order.OrderId}/notifications")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        (await client.PostAsync($"/api/orders/{order.OrderId}/dispatch", null)).EnsureSuccessStatusCode();

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var beforeCancel = await ReadAsync<List<NotificationDto>>(
            await client.GetAsync($"/api/orders/{order.OrderId}/notifications"));
        Assert.AreEqual(3, beforeCancel.Count);
        var placed = beforeCancel.Single(x => x.Kind == nameof(NotificationKind.OrderPlaced));
        Assert.AreEqual("undelivered", placed.Outcome);
        Assert.AreEqual("scheduled", beforeCancel.Single(x =>
            x.Kind == nameof(NotificationKind.DeliveryFollowUp)).Outcome);

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        var firstResend = await ReadAsync<NotificationCreatedResponse>(await client.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend", new { idempotencyKey = "attempt-1" }));
        var repeatedResend = await ReadAsync<NotificationCreatedResponse>(await client.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend", new { idempotencyKey = "attempt-1" }));
        Assert.AreEqual(firstResend.NotificationId, repeatedResend.NotificationId);
        Assert.AreEqual(4, provider.SendCount);

        var secondResend = await ReadAsync<NotificationCreatedResponse>(await client.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend", new { idempotencyKey = "attempt-2" }));
        Assert.AreNotEqual(firstResend.NotificationId, secondResend.NotificationId);
        Assert.AreEqual(5, provider.SendCount);

        var disposeResponse = await client.DeleteAsync(
            $"/api/notifications/{firstResend.NotificationId}/content");
        Assert.AreEqual(HttpStatusCode.NoContent, disposeResponse.StatusCode);
        Assert.IsTrue(provider.Redacted.Count > 0);

        (await client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.AreEqual(1, provider.CancelCount);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var reconciliation = await ReadAsync<ReconciliationResponse>(await client.GetAsync(
            $"/api/notifications/reconciliation?from={from}&to={to}"));
        Assert.IsTrue(reconciliation.Entries.Any(x => x.Presence == "matched"));

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var afterCancel = await ReadAsync<List<NotificationDto>>(
            await client.GetAsync($"/api/orders/{order.OrderId}/notifications"));
        Assert.AreEqual("canceled", afterCancel.Single(x =>
            x.Kind == nameof(NotificationKind.DeliveryFollowUp)).Outcome);
        Assert.IsNull(afterCancel.Single(x => x.NotificationId == firstResend.NotificationId).Content);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);
        var contacts = await ReadAsync<List<ContactNumberDto>>(await client.GetAsync("/api/contact-numbers"));
        Assert.AreEqual(0, contacts.Count);
        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/notifications/{placed.NotificationId}/resend", new { idempotencyKey = "after-delete" })).StatusCode);
    }

    [TestMethod]
    public async Task ProviderSendFailureDoesNotFailOrderPlacement()
    {
        var provider = new FakeProvider { FailSends = true };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPhoneNumberValidator>();
                services.RemoveAll<IMessageProvider>();
                services.AddSingleton<IPhoneNumberValidator>(provider);
                services.AddSingleton<IMessageProvider>(provider);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        (await client.PostAsJsonAsync("/api/contact-numbers", new { number = "typed number" }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var order = await ReadAsync<OrderCreatedResponse>(response);
        var notifications = await ReadAsync<List<NotificationDto>>(
            await client.GetAsync($"/api/orders/{order.OrderId}/notifications"));
        Assert.AreEqual("provider-error", notifications.Single().Outcome);
    }

    private static void SetToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private sealed class FakeProvider : IMessageProvider, IPhoneNumberValidator
    {
        private int _next;
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
        public int SendCount => _next;
        public int CancelCount { get; private set; }
        public ConcurrentBag<string> Redacted { get; } = new();
        public bool FailSends { get; init; }

        public Task<PhoneNumberValidation> ValidateAsync(string number,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PhoneNumberValidation(true, "+15555550100", Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string destination, string body,
            DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
        {
            if (FailSends) throw new ProviderRequestException("test send");
            var current = Interlocked.Increment(ref _next);
            var sid = $"SM{current:x32}";
            var status = sendAt.HasValue ? "scheduled" :
                body.Contains("has been placed", StringComparison.Ordinal) && current == 1 ? "undelivered" : "delivered";
            var now = DateTimeOffset.UtcNow;
            var message = new ProviderMessage(sid, status, status == "undelivered" ? 30006 : null,
                now, sendAt.HasValue ? null : now);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchAsync(string providerMessageSid,
            CancellationToken cancellationToken = default) => Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessage> CancelAsync(string providerMessageSid,
            CancellationToken cancellationToken = default)
        {
            CancelCount++;
            var existing = _messages[providerMessageSid];
            var updated = existing with { Status = "canceled" };
            _messages[providerMessageSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessage> RedactContentAsync(string providerMessageSid,
            CancellationToken cancellationToken = default)
        {
            Redacted.Add(providerMessageSid);
            return Task.FromResult(_messages[providerMessageSid]);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.ToList());
    }
}
