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
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task CompleteFlowEnforcesOwnershipRolesIdempotencyCancellationAndDisposal()
    {
        var provider = new FakeTwilioProvider();
        await using var factory = new NotificationApiFactory(provider);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var otherShopper = factory.CreateClient();
        otherShopper.DefaultRequestHeaders.Authorization = Bearer(CreateToken("other@example.com"));
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var invalid = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "bad" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { phoneNumber = "5550000000", countryCode = "US" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await contactResponse.Content.ReadFromJsonAsync<ContactResponse>();
        Assert.IsNotNull(contact);
        Assert.IsTrue(contact.ContactNumberId > 0);
        Assert.AreEqual("+15550000000", contact.PhoneNumber);

        var otherList = await otherShopper.GetFromJsonAsync<List<ContactResponse>>("/api/contact-numbers");
        Assert.AreEqual(0, otherList!.Count);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await otherShopper.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shipToAddress = new
            {
                street = "1 Main St", city = "Ottawa", state = "ON", country = "Canada", zipCode = "K1A0B1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.IsNotNull(order);
        Assert.IsTrue(order.OrderId > 0);
        Assert.AreEqual(1, order.Notifications.Count);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await otherShopper.GetAsync($"/api/orders/{order.OrderId}/notifications")).StatusCode);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{order.OrderId}/dispatch", null)).StatusCode);
        var dispatched = await admin.PostAsync($"/api/orders/{order.OrderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.OK, dispatched.StatusCode);
        Assert.AreEqual(1, provider.Messages.Values.Count(x => x.Status == "scheduled"));

        var cancelled = await admin.PostAsync($"/api/orders/{order.OrderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.AreEqual(1, provider.Messages.Values.Count(x => x.Status == "canceled"));

        var notifications = await shopper.GetFromJsonAsync<List<NotificationResponse>>(
            $"/api/orders/{order.OrderId}/notifications");
        Assert.IsNotNull(notifications);
        Assert.AreEqual(4, notifications.Count);
        var failed = notifications.First(x => x.ProviderStatus == "undelivered");

        var resendOne = await admin.PostAsJsonAsync($"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "attempt-1" });
        Assert.AreEqual(HttpStatusCode.OK, resendOne.StatusCode);
        var firstResend = await resendOne.Content.ReadFromJsonAsync<ResendResponse>();
        var duplicate = await (await admin.PostAsJsonAsync($"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "attempt-1" })).Content.ReadFromJsonAsync<ResendResponse>();
        Assert.AreEqual(firstResend!.NotificationId, duplicate!.NotificationId);

        var fresh = await (await admin.PostAsJsonAsync($"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "attempt-2" })).Content.ReadFromJsonAsync<ResendResponse>();
        Assert.AreNotEqual(firstResend.NotificationId, fresh!.NotificationId);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{firstResend.NotificationId}/content")).StatusCode);
        Assert.AreEqual(string.Empty, provider.Messages[firstResend.ProviderMessageId!].Body);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var report = await admin.GetFromJsonAsync<ReconciliationResponse>(
            $"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.IsNotNull(report);
        Assert.IsTrue(report.Entries.Any(x => x.Presence == "matched"));

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);
        var contactsAfterDelete = await shopper.GetFromJsonAsync<List<ContactResponse>>("/api/contact-numbers");
        Assert.AreEqual(0, contactsAfterDelete!.Count);
        var messageCount = provider.Messages.Count;
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/notifications/{failed.NotificationId}/resend",
                new { idempotencyKey = "after-contact-removal" })).StatusCode);
        Assert.AreEqual(messageCount, provider.Messages.Count);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static string CreateToken(string username)
    {
        var method = typeof(ApiTokenHelper).GetMethod("CreateToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { username, Array.Empty<string>() })!;
    }

    private sealed record ContactResponse(int ContactNumberId, string PhoneNumber);
    private sealed record OrderResponse(int OrderId, List<NotificationResponse> Notifications);
    private sealed record NotificationResponse(int NotificationId, string? ProviderMessageId, string ProviderStatus);
    private sealed record ResendResponse(int NotificationId, string? ProviderMessageId);
    private sealed record ReconciliationResponse(List<ReconciliationEntryResponse> Entries);
    private sealed record ReconciliationEntryResponse(string Presence);
}

internal sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    private readonly FakeTwilioProvider _provider;
    public NotificationApiFactory(FakeTwilioProvider provider) => _provider = provider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPhoneNumberValidator>();
            services.RemoveAll<IMessageProvider>();
            services.AddSingleton<IPhoneNumberValidator>(_provider);
            services.AddSingleton<IMessageProvider>(_provider);
        });
    }
}

internal sealed class FakeTwilioProvider : IPhoneNumberValidator, IMessageProvider
{
    private int _sequence;
    public ConcurrentDictionary<string, FakeMessage> Messages { get; } = new();

    public Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(phoneNumber == "bad"
            ? new PhoneNumberValidationResult(false, null, null, "invalid")
            : new PhoneNumberValidationResult(true, "+1" + new string(phoneNumber.Where(char.IsDigit).ToArray()), "US", null));

    public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var id = "SM" + Interlocked.Increment(ref _sequence).ToString().PadLeft(32, '0');
        var status = sendAt.HasValue ? "scheduled" : "undelivered";
        var message = new FakeMessage(id, status, "+15005550006", destination, body,
            DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
        Messages[id] = message;
        return Task.FromResult(message.ToProvider());
    }

    public Task<ProviderMessage> FetchAsync(string providerMessageId,
        CancellationToken cancellationToken = default) => Task.FromResult(Messages[providerMessageId].ToProvider());

    public Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        Messages[providerMessageId].Status = "canceled";
        return Task.FromResult(Messages[providerMessageId].ToProvider());
    }

    public Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        Messages[providerMessageId].Body = string.Empty;
        return Task.FromResult(Messages[providerMessageId].ToProvider());
    }

    public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProviderMessage>>(
        Messages.Values.Where(x => x.CreatedAt >= from && x.CreatedAt <= to).Select(x => x.ToProvider()).ToList());
}

internal sealed class FakeMessage
{
    public FakeMessage(string id, string status, string from, string to, string body,
        DateTimeOffset createdAt, DateTimeOffset? sentAt)
    {
        Id = id; Status = status; From = from; To = to; Body = body; CreatedAt = createdAt; SentAt = sentAt;
    }
    public string Id { get; }
    public string Status { get; set; }
    public string From { get; }
    public string To { get; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? SentAt { get; }
    public ProviderMessage ToProvider() => new(Id, Status, From, To, Body, CreatedAt, SentAt,
        Status == "undelivered" ? 30007 : null, Status == "undelivered" ? "blocked" : null);
}
