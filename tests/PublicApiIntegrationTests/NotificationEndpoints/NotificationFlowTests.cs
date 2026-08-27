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
public sealed class NotificationFlowTests
{
    [TestMethod]
    public async Task DrivesOrderNotificationLifecycleWithAuthorizationOwnershipAndIdempotency()
    {
        var provider = new FakeMessageProvider();
        await using WebApplicationFactory<Program> application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMessageProvider>();
                services.AddSingleton<IMessageProvider>(provider);
            }));
        using HttpClient client = application.CreateClient();
        SetToken(client, ApiTokenHelper.GetNormalUserToken());

        HttpResponseMessage contactResponse = await client.PostAsJsonAsync(
            "/api/contact-numbers", new { phoneNumber = "+16045550123" });
        Assert.AreEqual(HttpStatusCode.Created, contactResponse.StatusCode);
        int contactNumberId = ReadInt(await contactResponse.Content.ReadAsStringAsync(), "contactNumberId");

        HttpResponseMessage orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Main Street",
                city = "Vancouver",
                state = "BC",
                country = "Canada",
                zipCode = "V5K0A1"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        int orderId = ReadInt(await orderResponse.Content.ReadAsStringAsync(), "orderId");
        Assert.AreEqual(1, provider.CreatedCount);

        HttpResponseMessage forbiddenDispatch = await client.PostAsync($"/api/orders/{orderId}/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDispatch.StatusCode);

        SetToken(client, TokenFor("another-shopper@example.com"));
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/orders/{orderId}/notifications")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/dispatch", null)).StatusCode);
        Assert.AreEqual(3, provider.CreatedCount);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        JsonElement dispatchedNotifications = await ReadRootAsync(
            await client.GetAsync($"/api/orders/{orderId}/notifications"));
        JsonElement notificationArray = dispatchedNotifications.GetProperty("notifications");
        Assert.AreEqual(3, notificationArray.GetArrayLength());
        JsonElement scheduled = notificationArray.EnumerateArray().Single(x => x.GetProperty("isScheduled").GetBoolean());
        Assert.AreEqual("scheduled", scheduled.GetProperty("providerStatus").GetString());
        int failedNotificationId = notificationArray.EnumerateArray()
            .First(x => x.GetProperty("kind").GetString() == "OrderPlaced")
            .GetProperty("notificationId").GetInt32();

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.OK,
            (await client.PostAsync($"/api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.AreEqual(4, provider.CreatedCount);

        var resendBody = new { idempotencyKey = "same-request" };
        HttpResponseMessage firstResend = await client.PostAsJsonAsync(
            $"/api/notifications/{failedNotificationId}/resend", resendBody);
        Assert.AreEqual(HttpStatusCode.Created, firstResend.StatusCode);
        int resentNotificationId = ReadInt(await firstResend.Content.ReadAsStringAsync(), "notificationId");
        Assert.AreEqual(5, provider.CreatedCount);

        HttpResponseMessage repeatedResend = await client.PostAsJsonAsync(
            $"/api/notifications/{failedNotificationId}/resend", resendBody);
        Assert.AreEqual(HttpStatusCode.Created, repeatedResend.StatusCode);
        Assert.AreEqual(resentNotificationId,
            ReadInt(await repeatedResend.Content.ReadAsStringAsync(), "notificationId"));
        Assert.AreEqual(5, provider.CreatedCount);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/contact-numbers/{contactNumberId}")).StatusCode);
        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(
                $"/api/notifications/{failedNotificationId}/resend",
                new { idempotencyKey = "fresh-request" })).StatusCode);
        Assert.AreEqual(5, provider.CreatedCount);

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/notifications/{resentNotificationId}/content")).StatusCode);
        Assert.IsTrue(provider.DisposedSids.Count > 0);

        DateTimeOffset from = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset to = DateTimeOffset.UtcNow.AddMinutes(5);
        HttpResponseMessage reconciliation = await client.GetAsync(
            $"/api/notifications/reconciliation?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        JsonElement report = await ReadRootAsync(reconciliation);
        Assert.IsTrue(report.GetProperty("matched").GetInt32() >= 1);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        JsonElement finalNotifications = await ReadRootAsync(
            await client.GetAsync($"/api/orders/{orderId}/notifications"));
        JsonElement finalArray = finalNotifications.GetProperty("notifications");
        JsonElement cancelledFollowUp = finalArray.EnumerateArray()
            .Single(x => x.GetProperty("kind").GetString() == "DeliveryFollowUp");
        Assert.AreEqual("canceled", cancelledFollowUp.GetProperty("providerStatus").GetString());
        JsonElement disposed = finalArray.EnumerateArray()
            .Single(x => x.GetProperty("notificationId").GetInt32() == resentNotificationId);
        Assert.AreEqual(JsonValueKind.Null, disposed.GetProperty("content").ValueKind);
    }

    private static void SetToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string TokenFor(string userName)
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName) };
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY));
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static int ReadInt(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(propertyName).GetInt32();
    }

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class FakeMessageProvider : IMessageProvider
    {
        private int _nextId;
        private readonly ConcurrentDictionary<string, ProviderMessage> _messages = new();

        public int CreatedCount { get; private set; }
        public ConcurrentBag<string> DisposedSids { get; } = new();

        public Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(phoneNumber);

        public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken) =>
            Task.FromResult(Create(body, "undelivered", DateTimeOffset.UtcNow));

        public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
            Task.FromResult(Create(body, "scheduled", null));

        public Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken)
        {
            ProviderMessage current = _messages[providerSid];
            ProviderMessage cancelled = current with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerSid] = cancelled;
            return Task.FromResult(cancelled);
        }

        public Task<ProviderMessage> GetAsync(string providerSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerSid]);

        public Task<ProviderMessage> DisposeContentAsync(string providerSid, CancellationToken cancellationToken)
        {
            ProviderMessage current = _messages[providerSid];
            ProviderMessage disposed = current with { Body = null, DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerSid] = disposed;
            DisposedSids.Add(providerSid);
            return Task.FromResult(disposed);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values
                .Where(x => x.DateSent >= from && x.DateSent <= to)
                .ToList());

        private ProviderMessage Create(string body, string status, DateTimeOffset? dateSent)
        {
            string sid = $"SM{Interlocked.Increment(ref _nextId):D8}";
            CreatedCount++;
            var message = new ProviderMessage(
                sid, status, body, "+15005550006", status == "undelivered" ? 30007 : null,
                DateTimeOffset.UtcNow, dateSent, DateTimeOffset.UtcNow);
            _messages[sid] = message;
            return message;
        }
    }
}
