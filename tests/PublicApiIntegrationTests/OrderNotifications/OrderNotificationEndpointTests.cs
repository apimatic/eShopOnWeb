using System;
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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationEndpointTests
{
    [TestMethod]
    public async Task ApiReturnsIdentifiersEnforcesOwnershipAndRestrictsOperatorActions()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton<ISmsProvider, EndpointFakeSmsProvider>();
            }));
        using var client = factory.CreateClient();
        var shopperToken = await TokenAsync(client, "demouser@microsoft.com");
        var adminToken = await TokenAsync(client, "admin@microsoft.com");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shopperToken);
        var invalid = await client.PostAsJsonAsync("/api/contact-numbers", new { number = "invalid" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        var contactResponse = await client.PostAsJsonAsync("/api/contact-numbers", new { number = "+14165550100" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await contactResponse.Content.ReadFromJsonAsync<CreateIdResponse>();
        Assert.IsTrue(contact!.ContactNumberId > 0);

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new { street = "1 Main St", city = "Toronto", state = "ON", country = "CA", zipCode = "M5V 1A1" }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<CreateIdResponse>();
        Assert.IsTrue(order!.OrderId > 0);

        var shopperDispatch = await client.PostAsJsonAsync($"/api/orders/{order.OrderId}/dispatch", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperDispatch.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminCannotDeleteShopperContact = await client.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}");
        Assert.AreEqual(HttpStatusCode.NotFound, adminCannotDeleteShopperContact.StatusCode);
        var adminCannotReadShopperOrder = await client.GetAsync($"/api/orders/{order.OrderId}/notifications");
        Assert.AreEqual(HttpStatusCode.NotFound, adminCannotReadShopperOrder.StatusCode);
        var adminDispatch = await client.PostAsJsonAsync($"/api/orders/{order.OrderId}/dispatch", new { });
        Assert.AreEqual(HttpStatusCode.OK, adminDispatch.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shopperToken);
        var notifications = await client.GetFromJsonAsync<List<NotificationIdResponse>>($"/api/orders/{order.OrderId}/notifications");
        Assert.AreEqual(3, notifications!.Count);
        Assert.IsTrue(notifications.All(x => x.NotificationId > 0));
    }

    private static async Task<string> TokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/authenticate", new { username, password = "Pass@word1" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return result!.Token;
    }

    private sealed class TokenResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    private sealed class CreateIdResponse
    {
        public int ContactNumberId { get; set; }
        public int OrderId { get; set; }
    }

    private sealed class NotificationIdResponse
    {
        public int NotificationId { get; set; }
    }

    private sealed class EndpointFakeSmsProvider : ISmsProvider
    {
        private int _sequence;
        private readonly Dictionary<string, ProviderMessage> _messages = new(StringComparer.Ordinal);

        public Task<PhoneNumberValidation> ValidateDestinationAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken)
            => Task.FromResult(rawNumber == "invalid"
                ? new PhoneNumberValidation(false, null, new[] { "NOT_A_NUMBER" })
                : new PhoneNumberValidation(true, rawNumber, Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            var sid = $"SMENDPOINT{++_sequence:000000000000000000000000}";
            var message = new ProviderMessage(sid, sendAt.HasValue ? "scheduled" : "queued", body, "+15005550000", destination, null, DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
            => Task.FromResult(_messages[providerMessageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var message = _messages[providerMessageSid] with { Status = "canceled" };
            _messages[providerMessageSid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var message = _messages[providerMessageSid] with { Body = string.Empty };
            _messages[providerMessageSid] = message;
            return Task.FromResult(message);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values
                .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
                .ToList());
    }
}
