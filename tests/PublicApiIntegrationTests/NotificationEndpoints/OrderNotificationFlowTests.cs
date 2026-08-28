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

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTests
{
    [TestMethod]
    public async Task ContactNumbersAreCanonicalAndShopperScoped()
    {
        await using var factory = new NotificationApiFactory();
        using var first = factory.CreateAuthenticatedClient("first@example.test");
        using var second = factory.CreateAuthenticatedClient("second@example.test");

        var created = await first.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "typed-number" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var id = createdJson.RootElement.GetProperty("contactNumberId").GetInt32();
        Assert.AreEqual(FakeTwilioMessagingClient.CanonicalNumber,
            createdJson.RootElement.GetProperty("phoneNumber").GetString());

        var otherList = await second.GetFromJsonAsync<JsonElement>("/api/contact-numbers");
        Assert.AreEqual(0, otherList.GetProperty("contactNumbers").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await second.DeleteAsync($"/api/contact-numbers/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await first.DeleteAsync($"/api/contact-numbers/{id}")).StatusCode);
    }

    [TestMethod]
    public async Task DispatchSchedulesAtProviderAndCancelCancelsFollowUp()
    {
        await using var factory = new NotificationApiFactory();
        using var shopper = factory.CreateAuthenticatedClient("flow@example.test");
        using var admin = factory.CreateAdminClient();
        await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "typed-number" });

        var placed = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, placed.StatusCode);
        var placedJson = await JsonDocument.ParseAsync(await placed.Content.ReadAsStreamAsync());
        var orderId = placedJson.RootElement.GetProperty("orderId").GetInt32();

        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        var scheduled = factory.Twilio.Messages.Values.Single(x => x.SendAt.HasValue);
        Assert.IsTrue(scheduled.SendAt > DateTimeOffset.UtcNow.AddDays(2.9));

        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual("canceled", scheduled.Status);

        var notifications = await shopper.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{orderId}/notifications");
        var kinds = notifications.GetProperty("notifications").EnumerateArray()
            .Select(x => x.GetProperty("type").GetString()).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "OrderPlaced", "OrderDispatched", "DeliveryFollowUp", "OrderCancelled" }, kinds);
    }

    [TestMethod]
    public async Task ResendIsIdempotentAndContentIsRedactedAtProvider()
    {
        await using var factory = new NotificationApiFactory { InitialStatus = "undelivered" };
        using var shopper = factory.CreateAuthenticatedClient("resend@example.test");
        using var admin = factory.CreateAdminClient();
        await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "typed-number" });
        var placed = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        var placedJson = await JsonDocument.ParseAsync(await placed.Content.ReadAsStreamAsync());
        var orderId = placedJson.RootElement.GetProperty("orderId").GetInt32();
        var notifications = await shopper.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{orderId}/notifications");
        var originalId = notifications.GetProperty("notifications")[0].GetProperty("notificationId").GetInt32();

        var first = await admin.PostAsJsonAsync($"/api/notifications/{originalId}/resend",
            new { idempotencyKey = "same-operation" });
        var second = await admin.PostAsJsonAsync($"/api/notifications/{originalId}/resend",
            new { idempotencyKey = "same-operation" });
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var firstJson = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        var secondJson = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.AreEqual(firstJson.RootElement.GetProperty("notificationId").GetInt32(),
            secondJson.RootElement.GetProperty("notificationId").GetInt32());
        Assert.AreEqual(2, factory.Twilio.Messages.Count);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{originalId}/content")).StatusCode);
        Assert.IsNull(factory.Twilio.Messages.Values.OrderBy(x => x.CreatedAt).First().Body);
        var after = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var original = after.GetProperty("notifications").EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == originalId);
        Assert.AreEqual(JsonValueKind.Null, original.GetProperty("content").ValueKind);
        Assert.IsTrue(original.GetProperty("contentRedacted").GetBoolean());
    }

    [TestMethod]
    public async Task ProviderFailureDoesNotFailOrderTransitions()
    {
        await using var factory = new NotificationApiFactory { ThrowOnSend = true };
        using var shopper = factory.CreateAuthenticatedClient("failure@example.test");
        using var admin = factory.CreateAdminClient();
        await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "typed-number" });

        var placed = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, placed.StatusCode);
        var placedJson = await JsonDocument.ParseAsync(await placed.Content.ReadAsStreamAsync());
        var orderId = placedJson.RootElement.GetProperty("orderId").GetInt32();
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);

        var details = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        Assert.IsTrue(details.GetProperty("notifications").EnumerateArray()
            .All(x => x.GetProperty("status").GetString() == "local-failed"));
        var myOrders = await shopper.GetFromJsonAsync<JsonElement>("/api/my-orders");
        Assert.AreEqual("Cancelled", myOrders.GetProperty("orders")[0].GetProperty("status").GetString());
    }
}

internal sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeTwilioMessagingClient Twilio { get; } = new();
    public string InitialStatus { set => Twilio.InitialStatus = value; }
    public bool ThrowOnSend { set => Twilio.ThrowOnSend = value; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITwilioMessagingClient>();
            services.AddSingleton<ITwilioMessagingClient>(Twilio);
        });
    }

    public HttpClient CreateAuthenticatedClient(string username)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(username));
        return client;
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }
}

internal sealed class FakeTwilioMessagingClient : ITwilioMessagingClient
{
    public const string CanonicalNumber = "+10000000000";
    private int _sequence;
    public string InitialStatus { get; set; } = "delivered";
    public bool ThrowOnSend { get; set; }
    public ConcurrentDictionary<string, FakeMessage> Messages { get; } = new();

    public Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ValidatedPhoneNumber(input != "invalid", CanonicalNumber));

    public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (ThrowOnSend)
        {
            throw new TwilioProviderException(502);
        }

        var sid = $"SM{Interlocked.Increment(ref _sequence):D32}";
        var now = DateTimeOffset.UtcNow;
        var message = new FakeMessage(sid, body, sendAt.HasValue ? "scheduled" : InitialStatus, now,
            sendAt, sendAt.HasValue ? null : now);
        Messages[sid] = message;
        return Task.FromResult(ToProvider(message));
    }

    public Task<ProviderMessage> GetMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken) => Task.FromResult(ToProvider(Messages[providerMessageSid]));

    public Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken)
    {
        Messages[providerMessageSid].Status = "canceled";
        return Task.FromResult(ToProvider(Messages[providerMessageSid]));
    }

    public Task RedactMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        Messages[providerMessageSid].Body = null;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderMessage>>(
        Messages.Values.Where(x => x.SentAt >= from && x.SentAt <= to).Select(ToProvider).ToList());

    private static ProviderMessage ToProvider(FakeMessage message) =>
        new(message.Sid, message.Status, message.Status == "undelivered" ? 30034 : null,
            message.CreatedAt, message.SentAt);
}

internal sealed class FakeMessage
{
    public FakeMessage(string sid, string? body, string status, DateTimeOffset createdAt,
        DateTimeOffset? sendAt, DateTimeOffset? sentAt)
    {
        Sid = sid;
        Body = body;
        Status = status;
        CreatedAt = createdAt;
        SendAt = sendAt;
        SentAt = sentAt;
    }

    public string Sid { get; }
    public string? Body { get; set; }
    public string Status { get; set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? SendAt { get; }
    public DateTimeOffset? SentAt { get; }
}
