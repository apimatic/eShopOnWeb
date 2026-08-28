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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class OrderNotificationFlowTest
{
    [TestMethod]
    public async Task ProviderFailureNeverRollsBackOrderTransitions()
    {
        var provider = new FakeMessageProvider { ThrowOnSend = true };
        await using var factory = new NotificationApiFactory(provider);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        Assert.AreEqual(HttpStatusCode.Created, (await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { phoneNumber = "not-a-real-number" })).StatusCode);
        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new
            {
                street = "Test street", city = "Test city", state = "", country = "CA", zipCode = "A1A1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await ReadJson(orderResponse)).GetProperty("orderId").GetInt32();
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);

        var notifications = await ReadJson(await shopper.GetAsync($"/api/orders/{orderId}/notifications"));
        Assert.IsTrue(notifications.EnumerateArray().All(x =>
            x.GetProperty("providerStatus").GetString() == "provider-error"));
        var orders = await ReadJson(await shopper.GetAsync("/api/my-orders"));
        Assert.AreEqual("cancelled", orders.EnumerateArray().Single().GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task CompleteFlowIsAuthorizedDurableAndIdempotent()
    {
        var provider = new FakeMessageProvider();
        await using var factory = new NotificationApiFactory(provider);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("/api/contact-numbers",
            new { phoneNumber = "not-a-real-number" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        var contactId = (await ReadJson(contactResponse)).GetProperty("contactNumberId").GetInt32();

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "Test street", city = "Test city", state = "", country = "CA", zipCode = "A1A1A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await ReadJson(orderResponse)).GetProperty("orderId").GetInt32();

        var forbiddenDispatch = await shopper.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);

        var notificationsResponse = await shopper.GetAsync($"/api/orders/{orderId}/notifications");
        Assert.AreEqual(HttpStatusCode.OK, notificationsResponse.StatusCode);
        var notifications = await ReadJson(notificationsResponse);
        var placed = notifications.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "placed");
        var followUp = notifications.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "delivery-follow-up");
        Assert.AreEqual("undelivered", placed.GetProperty("providerStatus").GetString());
        Assert.AreEqual("scheduled", followUp.GetProperty("providerStatus").GetString());
        Assert.IsTrue(followUp.GetProperty("scheduledFor").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(2));

        var placedId = placed.GetProperty("notificationId").GetInt32();
        var firstResend = await admin.PostAsJsonAsync($"/api/notifications/{placedId}/resend",
            new { idempotencyKey = "same-attempt" });
        var secondResend = await admin.PostAsJsonAsync($"/api/notifications/{placedId}/resend",
            new { idempotencyKey = "same-attempt" });
        Assert.AreEqual(HttpStatusCode.OK, firstResend.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResend.StatusCode);
        var resendId = (await ReadJson(firstResend)).GetProperty("notificationId").GetInt32();
        Assert.AreEqual(resendId, (await ReadJson(secondResend)).GetProperty("notificationId").GetInt32());
        Assert.AreEqual(4, provider.SendCount, "The duplicate idempotency key must not make a fifth send.");

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{placedId}/content")).StatusCode);
        Assert.IsTrue(provider.RedactedIds.Count == 1);

        Assert.AreEqual(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        notifications = await ReadJson(await shopper.GetAsync($"/api/orders/{orderId}/notifications"));
        followUp = notifications.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "delivery-follow-up");
        Assert.AreEqual("canceled", followUp.GetProperty("providerStatus").GetString());

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await admin.GetAsync($"/api/notifications/reconciliation?from={from}&to={to}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        Assert.IsTrue((await ReadJson(reconciliation)).GetProperty("matched").GetInt32() > 0);

        using var otherShopper = factory.CreateClient();
        otherShopper.DefaultRequestHeaders.Authorization = Bearer(CreateOtherShopperToken());
        Assert.AreEqual(0, (await ReadJson(await otherShopper.GetAsync("/api/contact-numbers"))).GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await otherShopper.DeleteAsync($"/api/contact-numbers/{contactId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await otherShopper.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static string CreateOtherShopperToken()
    {
        // Reuse the production token helper shape while changing only the identity claim.
        return TokenFactory.Create("other-shopper@example.invalid");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}

internal sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    private readonly IMessageProvider _provider;
    private readonly string _databaseSuffix = Guid.NewGuid().ToString("N");

    public NotificationApiFactory(IMessageProvider provider) => _provider = provider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessageProvider>();
            services.AddSingleton(_provider);
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.RemoveAll<CatalogContext>();
            services.AddDbContext<CatalogContext>(options =>
                options.UseInMemoryDatabase($"Catalog-{_databaseSuffix}"));
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase($"Identity-{_databaseSuffix}"));
        });
    }
}

internal sealed class FakeMessageProvider : IMessageProvider
{
    private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();
    private int _sequence;
    public int SendCount => _sequence;
    public List<string> RedactedIds { get; } = new();
    public bool ThrowOnSend { get; set; }

    public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken = default) => Task.FromResult(
        new PhoneNumberValidation(true, "+00000000000", Array.Empty<string>()));

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend) throw new MessageProviderException("test send");
        var number = Interlocked.Increment(ref _sequence);
        var id = $"SM{number:D32}";
        var status = sendAt.HasValue ? "scheduled" : body.Contains("has been placed") ? "undelivered" : "delivered";
        var message = new ProviderMessage(id, status, status == "undelivered" ? 30005 : null,
            DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
        _messages[id] = message;
        return Task.FromResult(message);
    }

    public Task<ProviderMessage> GetAsync(string providerMessageId,
        CancellationToken cancellationToken = default) => Task.FromResult(_messages[providerMessageId]);

    public Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        var current = _messages[providerMessageId];
        var updated = current with { Status = "canceled" };
        _messages[providerMessageId] = updated;
        return Task.FromResult(updated);
    }

    public Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        RedactedIds.Add(providerMessageId);
        return Task.FromResult(_messages[providerMessageId]);
    }

    public Task<IReadOnlyCollection<ProviderMessage>> ListApplicationMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ProviderMessage>>(_messages.Values.ToArray());
}

internal static class TokenFactory
{
    public static string Create(string userName)
    {
        var key = System.Text.Encoding.ASCII.GetBytes(
            Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
