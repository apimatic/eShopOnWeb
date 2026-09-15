using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowEndpointTest
{
    [TestMethod]
    public async Task DrivesAuthorizeCaptureRefundAndSavedCardWithoutCrossCustomerAccess()
    {
        var gateway = new FakePayPalGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(gateway);
            }));
        using var client = factory.CreateClient();
        SetToken(client, ApiTokenHelper.GetNormalUserToken());

        var firstOrder = await PlaceOrder(client);
        var oneOffPay = await client.PostAsJsonAsync($"api/orders/{firstOrder}/pay", new
        {
            card = TestCard(),
            paymentMethodId = (int?)null
        });
        Assert.AreEqual(HttpStatusCode.OK, oneOffPay.StatusCode);

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        var fulfil = await client.PostAsync($"api/orders/{firstOrder}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var refund1 = await client.PostAsJsonAsync($"api/orders/{firstOrder}/refunds",
            new { amount = 2.50m, idempotencyKey = "refund-one" });
        var refundReplay = await client.PostAsJsonAsync($"api/orders/{firstOrder}/refunds",
            new { amount = 2.50m, idempotencyKey = "refund-one" });
        Assert.AreEqual(HttpStatusCode.Created, refund1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, refundReplay.StatusCode);
        Assert.AreEqual(1, gateway.RefundCalls);

        var save = await client.PostAsJsonAsync("api/payment-methods", new { card = TestCard() });
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.IsNotNull(saved);
        Assert.AreEqual("1111", saved.LastDigits);

        var secondOrder = await PlaceOrder(client);
        var savedPay = await client.PostAsJsonAsync($"api/orders/{secondOrder}/pay", new
        {
            card = (object?)null,
            paymentMethodId = saved.PaymentMethodId
        });
        Assert.AreEqual(HttpStatusCode.OK, savedPay.StatusCode);

        SetToken(client, TokenFor("another-shopper@example.com"));
        var crossOrder = await client.PostAsJsonAsync($"api/orders/{secondOrder}/pay", new
        {
            card = (object?)null,
            paymentMethodId = saved.PaymentMethodId
        });
        var crossDelete = await client.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossOrder.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, crossDelete.StatusCode);

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var delete = await client.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var list = await client.GetFromJsonAsync<PaymentMethodsResponse>("api/payment-methods");
        Assert.IsNotNull(list);
        Assert.AreEqual(0, list.PaymentMethods.Count);
    }

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } },
            shippingAddress = new
            {
                firstName = "Sandbox",
                lastName = "Shopper",
                street = "1 Main Street",
                city = "San Jose",
                state = "CA",
                country = "US",
                zipCode = "95131"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>();
        Assert.IsNotNull(body);
        return body.OrderId;
    }

    private static object TestCard() => new
    {
        name = "Sandbox Shopper",
        number = "test-card-number",
        expiry = "2030-12",
        securityCode = "123",
        billingAddress = new
        {
            addressLine1 = "1 Main Street",
            addressLine2 = (string?)null,
            city = "San Jose",
            state = "CA",
            postalCode = "95131",
            countryCode = "US"
        }
    };

    private static void SetToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string TokenFor(string userName)
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName) };
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        private readonly Dictionary<string, ProviderPaymentMethod> _cards = new();
        public int RefundCalls { get; private set; }

        public Task<ProviderOrder> CreateOrderAsync(string amount, string currency, string invoiceId,
            string customId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderOrder($"ORDER-{requestId}", "CREATED"));

        public Task<ProviderAuthorization> AuthorizeAsync(string payPalOrderId, CardInput? card,
            string? vaultId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderAuthorization($"AUTH-{requestId}", "CREATED", 8.50m, "USD",
                "COMPLETED", DateTimeOffset.UtcNow.ToString("O"), null,
                DateTimeOffset.UtcNow.AddDays(29).ToString("O")));

        public Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(new ProviderAuthorization(
                authorizationId, "CREATED", 8.50m, "USD", null, DateTimeOffset.UtcNow.ToString("O"),
                null, DateTimeOffset.UtcNow.AddDays(29).ToString("O")));

        public Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            GetAuthorizationAsync(authorizationId, cancellationToken);

        public Task<ProviderCapture> CaptureAsync(string authorizationId, string amount,
            string currency, string requestId, CancellationToken cancellationToken) => Task.FromResult(
                new ProviderCapture($"CAP-{requestId}", "COMPLETED", decimal.Parse(amount, CultureInfo.InvariantCulture),
                    currency, decimal.Parse(amount, CultureInfo.InvariantCulture), .30m,
                    decimal.Parse(amount, CultureInfo.InvariantCulture) - .30m, DateTimeOffset.UtcNow.ToString("O"), null));

        public Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCapture(captureId, "COMPLETED", 8.50m, "USD", 8.50m, .30m, 8.20m,
                DateTimeOffset.UtcNow.ToString("O"), null));

        public Task<ProviderAuthorization> VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.FromResult(new ProviderAuthorization(
                authorizationId, "VOIDED", 8.50m, "USD", null, null, null, null));

        public Task<ProviderRefund> RefundAsync(string captureId, string? amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new ProviderRefund($"REF-{requestId}", "COMPLETED",
                amount == null ? 8.50m : decimal.Parse(amount, CultureInfo.InvariantCulture), currency,
                DateTimeOffset.UtcNow.ToString("O"), null));
        }

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderPaymentMethod> SaveCardAsync(string merchantCustomerId, string? providerCustomerId,
            CardInput card, string requestId, CancellationToken cancellationToken)
        {
            var method = new ProviderPaymentMethod("TOKEN-1", providerCustomerId ?? "CUSTOMER-1",
                "VISA", "1111", card.Expiry, "CREDIT");
            _cards[method.Id] = method;
            return Task.FromResult(method);
        }

        public Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderPaymentMethod>>(
                _cards.Values.Where(x => x.CustomerId == customerId).ToList());

        public Task<ProviderPaymentMethod> GetCardAsync(string tokenId, CancellationToken cancellationToken) =>
            Task.FromResult(_cards[tokenId]);

        public Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken)
        {
            _cards.Remove(tokenId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderTransaction>>(Array.Empty<ProviderTransaction>());
    }
}
