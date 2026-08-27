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
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationFlowTest
{
    [TestMethod]
    public async Task CompleteFlowEnforcesOwnershipCancellationAndResendIdempotency()
    {
        var twilio = new FakeTwilioGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton<ITwilioGateway>(twilio);
            });
        });
        using var shopper = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        using var admin = AuthenticatedClient(factory, ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "fake input" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await contactResponse.Content.ReadFromJsonAsync<RegisterContactNumberResponse>();
        Assert.IsNotNull(contact);
        Assert.AreEqual(FakeTwilioGateway.CanonicalNumber, contact.PhoneNumber);

        var placeResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, placeResponse.StatusCode);
        var placed = await placeResponse.Content.ReadFromJsonAsync<PlaceOrderResponse>();
        Assert.IsNotNull(placed);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{placed.OrderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{placed.OrderId}/dispatch", null)).StatusCode);

        var beforeCancel = await shopper.GetFromJsonAsync<List<NotificationResponse>>(
            $"/api/orders/{placed.OrderId}/notifications");
        Assert.IsNotNull(beforeCancel);
        Assert.AreEqual(3, beforeCancel.Count);
        var followUp = beforeCancel.Single(x => x.Kind == "DeliveryFollowUp");
        Assert.AreEqual("scheduled", followUp.ProviderStatus);

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/orders/{placed.OrderId}/notifications")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{placed.OrderId}/cancel", null)).StatusCode);

        var afterCancel = await shopper.GetFromJsonAsync<List<NotificationResponse>>(
            $"/api/orders/{placed.OrderId}/notifications");
        Assert.IsNotNull(afterCancel);
        Assert.AreEqual("canceled", afterCancel.Single(x => x.NotificationId == followUp.NotificationId).ProviderStatus);

        var failed = afterCancel.First(x => x.Kind == "OrderPlaced");
        var firstResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "same-key" });
        var repeatedResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "same-key" });
        var freshResend = await admin.PostAsJsonAsync(
            $"/api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "fresh-key" });
        var first = await firstResend.Content.ReadFromJsonAsync<ResendNotificationResponse>();
        var repeated = await repeatedResend.Content.ReadFromJsonAsync<ResendNotificationResponse>();
        var fresh = await freshResend.Content.ReadFromJsonAsync<ResendNotificationResponse>();
        Assert.IsNotNull(first);
        Assert.IsNotNull(repeated);
        Assert.IsNotNull(fresh);
        Assert.AreEqual(first.NotificationId, repeated.NotificationId);
        Assert.AreNotEqual(first.NotificationId, fresh.NotificationId);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{first.NotificationId}/content")).StatusCode);
        Assert.IsTrue(twilio.Messages.Values.Any(x => x.Body is null or ""));

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}")).StatusCode);
        var sendCount = twilio.SendCount;
        await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        });
        Assert.AreEqual(sendCount, twilio.SendCount);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        public const string CanonicalNumber = "+15555550100";
        private int _sequence;
        public ConcurrentDictionary<string, TwilioMessage> Messages { get; } = new();
        public int SendCount => _sequence;

        public Task<PhoneNumberLookup> ValidatePhoneNumberAsync(string suppliedNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberLookup(true, CanonicalNumber, Array.Empty<string>()));

        public Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            var sequence = Interlocked.Increment(ref _sequence);
            var sid = "SM" + sequence.ToString("D32");
            var now = DateTimeOffset.UtcNow;
            var message = new TwilioMessage(
                sid,
                sendAt.HasValue ? "scheduled" : "undelivered",
                body,
                "+15555550101",
                to,
                sendAt.HasValue ? null : 30034,
                now,
                sendAt.HasValue ? null : now,
                sendAt);
            Messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[sid]);

        public Task<TwilioMessage> CancelScheduledMessageAsync(string sid, CancellationToken cancellationToken)
        {
            var updated = Messages[sid] with { Status = "canceled" };
            Messages[sid] = updated;
            return Task.FromResult(updated);
        }

        public Task<TwilioMessage> RedactMessageContentAsync(string sid, CancellationToken cancellationToken)
        {
            var updated = Messages[sid] with { Body = string.Empty };
            Messages[sid] = updated;
            return Task.FromResult(updated);
        }

        public Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TwilioMessage>>(Messages.Values.Where(x => x.DateSent >= from && x.DateSent <= to).ToList());
    }
}
