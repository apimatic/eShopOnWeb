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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class NotificationFlowTest
{
    [TestMethod]
    public async Task ShopperAndOperatorCanDriveOrderLifecycleAndCancellationStopsFollowUp()
    {
        var gateway = new FakeTwilioGateway();
        using var application = CreateApplication(gateway);
        using var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = application.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "5550000001" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await JsonDocument.ParseAsync(await orderResponse.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("orderId").GetInt32();

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(1, gateway.Scheduled.Count());
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual("canceled", gateway.Scheduled.Single().Status);

        var notifications = await shopper.GetAsync($"/api/orders/{orderId}/notifications");
        notifications.EnsureSuccessStatusCode();
        var json = await notifications.Content.ReadAsStringAsync();
        StringAssert.Contains(json, "DeliveryFollowUp");
        StringAssert.Contains(json, "canceled");
    }

    [TestMethod]
    public async Task ResendIsIdempotentAndDeletedDestinationCannotBeUsed()
    {
        var gateway = new FakeTwilioGateway { FailNextSend = true };
        using var application = CreateApplication(gateway);
        using var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = application.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contact = await shopper.PostAsJsonAsync("/api/contact-numbers", new { phoneNumber = "5550000001" });
        var contactDocument = await JsonDocument.ParseAsync(await contact.Content.ReadAsStreamAsync());
        var contactId = contactDocument.RootElement.GetProperty("contactNumberId").GetInt32();
        var order = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        var orderDocument = await JsonDocument.ParseAsync(await order.Content.ReadAsStreamAsync());
        var orderId = orderDocument.RootElement.GetProperty("orderId").GetInt32();
        var listed = await shopper.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/notifications");
        var notificationId = listed[0].GetProperty("notificationId").GetInt32();

        var first = await admin.PostAsJsonAsync($"/api/notifications/{notificationId}/resend",
            new { idempotencyKey = "same-operation" });
        var second = await admin.PostAsJsonAsync($"/api/notifications/{notificationId}/resend",
            new { idempotencyKey = "same-operation" });
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        Assert.AreEqual(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.AreEqual(2, gateway.SendCount); // failed original plus one resend

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await shopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        var freshKey = await admin.PostAsJsonAsync($"/api/notifications/{notificationId}/resend",
            new { idempotencyKey = "fresh-operation" });
        Assert.AreEqual(HttpStatusCode.Conflict, freshKey.StatusCode);
        Assert.AreEqual(2, gateway.SendCount);
    }

    private static WebApplicationFactory<Program> CreateApplication(FakeTwilioGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Twilio:AccountSid"] = "AC00000000000000000000000000000000",
                    ["Twilio:AuthToken"] = "test-only-placeholder",
                    ["Twilio:FromNumber"] = "+15550000000",
                    ["Twilio:MessagingServiceSid"] = "MG00000000000000000000000000000000"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITwilioGateway>();
                services.AddSingleton<ITwilioGateway>(gateway);
            });
        });

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed class FakeTwilioGateway : ITwilioGateway
    {
        private int _sid;
        public bool FailNextSend { get; set; }
        public int SendCount { get; private set; }
        public List<ProviderMessage> Messages { get; } = new();
        public IEnumerable<ProviderMessage> Scheduled => Messages.Where(x => x.DateSent is null);

        public Task<PhoneNumberValidation> ValidateMobileNumberAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidation(true, "+15550000001", null));

        public Task<ProviderMessage> SendMessageAsync(string destination, string body, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            SendCount++;
            if (FailNextSend)
            {
                FailNextSend = false;
                throw new TwilioProviderException("create message", 400, 30007, "Message rejected");
            }
            var message = new ProviderMessage($"SM{++_sid:D32}", "+15550000000", destination,
                sendAt.HasValue ? "scheduled" : "delivered", body, DateTimeOffset.UtcNow,
                sendAt.HasValue ? null : DateTimeOffset.UtcNow, null, null);
            Messages.Add(message);
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages.Single(x => x.Sid == messageSid));

        public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = Messages.Single(x => x.Sid == messageSid);
            var canceled = current with { Status = "canceled" };
            Messages[Messages.IndexOf(current)] = canceled;
            return Task.FromResult(canceled);
        }

        public Task<ProviderMessage> RedactMessageContentAsync(string messageSid, CancellationToken cancellationToken)
        {
            var current = Messages.Single(x => x.Sid == messageSid);
            var redacted = current with { Body = string.Empty };
            Messages[Messages.IndexOf(current)] = redacted;
            return Task.FromResult(redacted);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages);
    }
}
