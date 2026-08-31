using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IdentityModel.Tokens.Jwt;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentFlowTests
{
    [TestMethod]
    public async Task CompleteFlowIsOwnedIdempotentAndReportsProviderAccounting()
    {
        await using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        Authenticate(client, "shopper@example.test");

        var orderId = await PlaceOrder(client);
        var payBody = new
        {
            card = Card(),
            paymentMethodId = (int?)null
        };
        var paid = await client.PostAsJsonAsync($"/api/orders/{orderId}/pay", payBody);
        paid.EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/orders/{orderId}/pay", payBody)).EnsureSuccessStatusCode();
        Assert.AreEqual(1, factory.Gateway.AuthorizeCalls);

        var other = factory.CreateClient();
        Authenticate(other, "other@example.test");
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"/api/orders/{orderId}/pay", payBody)).StatusCode);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/orders/{orderId}/fulfil", null)).StatusCode);
        Authenticate(client, "admin@microsoft.com", "Administrators");
        var fulfilled = await client.PostAsync($"/api/orders/{orderId}/fulfil", null);
        fulfilled.EnsureSuccessStatusCode();
        var fulfilledJson = await fulfilled.Content.ReadAsStringAsync();
        StringAssert.Contains(fulfilledJson, "\"paymentState\":\"Fulfilled\"");
        StringAssert.Contains(fulfilledJson, "\"payPalFee\":0.50");
        StringAssert.Contains(fulfilledJson, "\"netProceeds\":19.00");

        Authenticate(client, "shopper@example.test");
        var refundBody = new { amount = 3.00m, idempotencyKey = "return-line-1" };
        var refund = await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds", refundBody);
        refund.EnsureSuccessStatusCode();
        var refundJson = await refund.Content.ReadAsStringAsync();
        StringAssert.Contains(refundJson, "\"refundId\":");
        (await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds", refundBody)).EnsureSuccessStatusCode();
        Assert.AreEqual(1, factory.Gateway.RefundCalls);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds",
                new { amount = 17.00m, idempotencyKey = "too-much" })).StatusCode);

        var save = await client.PostAsJsonAsync("/api/payment-methods", new { card = Card() });
        save.EnsureSuccessStatusCode();
        var saveJson = await save.Content.ReadAsStringAsync();
        var paymentMethodId = JsonDocument.Parse(saveJson).RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.IsFalse(saveJson.Contains("test-card-number", StringComparison.Ordinal));
        Assert.IsFalse(saveJson.Contains("security-code-secret", StringComparison.Ordinal));
        StringAssert.Contains(saveJson, "1111");

        var secondOrderId = await PlaceOrder(client);
        (await client.PostAsJsonAsync($"/api/orders/{secondOrderId}/pay",
            new { card = (object?)null, paymentMethodId })).EnsureSuccessStatusCode();
        Assert.AreEqual("vault-token-1", factory.Gateway.LastVaultId);

        (await client.DeleteAsync($"/api/payment-methods/{paymentMethodId}")).EnsureSuccessStatusCode();
        var methods = await client.GetStringAsync("/api/payment-methods");
        StringAssert.Contains(methods, "\"paymentMethods\":[]");
        var thirdOrderId = await PlaceOrder(client);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/orders/{thirdOrderId}/pay",
                new { card = (object?)null, paymentMethodId })).StatusCode);
    }

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("orderId").GetInt32();
    }

    private static object Card() => new
    {
        name = "Test Shopper",
        number = "test-card-number",
        expiry = "2030-12",
        securityCode = "security-code-secret",
        billingAddress = new
        {
            addressLine1 = "1 Test Street",
            addressLine2 = (string?)null,
            city = "Test City",
            state = "CA",
            postalCode = "90001",
            countryCode = "US"
        }
    };

    private static void Authenticate(HttpClient client, string user, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, user) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY)),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            handler.WriteToken(handler.CreateToken(descriptor)));
    }

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        public FakePayPalGateway Gateway { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["PayPal:ClientId"] = "test-only",
                    ["PayPal:ClientSecret"] = "test-only",
                    ["PayPal:Environment"] = "Sandbox",
                    ["PayPal:Currency"] = "USD"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(Gateway);
            });
        }
    }

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastVaultId { get; private set; }

        public Task<string> CreateOrderAsync(int orderId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken) => Task.FromResult($"provider-order-{orderId}");

        public Task<ProviderAuthorization> AuthorizeOrderAsync(string payPalOrderId, decimal amount,
            CardRequest? card, string? vaultId, string requestId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastVaultId = vaultId;
            return Task.FromResult(new ProviderAuthorization(payPalOrderId, $"auth-{payPalOrderId}",
                "CREATED", amount, DateTimeOffset.UtcNow.AddDays(29)));
        }

        public Task<ProviderAuthorization?> GetOrderAuthorizationAsync(string payPalOrderId,
            CancellationToken cancellationToken) => Task.FromResult<ProviderAuthorization?>(null);

        public Task<ProviderAuthorizationState> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(new ProviderAuthorizationState(
                authorizationId, "CREATED", 19.50m, DateTimeOffset.UtcNow.AddDays(29)));

        public Task<ProviderAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderAuthorizationState(authorizationId, "CREATED", amount,
                DateTimeOffset.UtcNow.AddDays(29)));

        public Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken) => Task.FromResult(new ProviderCapture(
                $"capture-{authorizationId}", "COMPLETED", amount, 0.50m, amount - 0.50m));

        public Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCapture(captureId, "COMPLETED", 19.50m, 0.50m, 19.00m));

        public Task<ProviderAuthorizationState> VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.FromResult(new ProviderAuthorizationState(
                authorizationId, "VOIDED", 19.50m, null));

        public Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
            string idempotencyKey, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new ProviderRefund($"refund-{idempotencyKey}", "COMPLETED", amount));
        }

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderRefund(refundId, "COMPLETED", 3.00m));

        public Task<ProviderVaultedCard> SaveCardAsync(CardRequest card, string setupRequestId,
            string tokenRequestId, CancellationToken cancellationToken) => Task.FromResult(
                new ProviderVaultedCard("setup-1", "vault-token-1", "customer-1", "ACTIVE",
                    "VISA", "1111", "2030-12", card.Name));

        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderTransaction>>(Array.Empty<ProviderTransaction>());
    }
}
