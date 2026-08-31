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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private FakePayPalGateway _payPal = null!;

    [TestInitialize]
    public void Initialize()
    {
        _payPal = new FakePayPalGateway();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.RemoveAll<IOptions<PayPalOptions>>();
                services.AddSingleton<IPayPalGateway>(_payPal);
                services.AddSingleton<IOptions<PayPalOptions>>(Options.Create(new PayPalOptions
                {
                    ClientId = "test-only",
                    ClientSecret = "test-only",
                    Environment = "sandbox",
                    Currency = "USD"
                }));
            }));
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task RequiresJwtAndAdministratorRoleForOperatorActions()
    {
        using var anonymous = _factory.CreateClient();
        var create = await anonymous.PostAsJsonAsync("api/orders", ValidOrder());
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);

        using var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var fulfil = await shopper.PostAsync("api/orders/1/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);

        using var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var report = await admin.GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");
        Assert.AreEqual(HttpStatusCode.OK, report.StatusCode);
    }

    [TestMethod]
    public async Task AuthorizeCaptureAndRefundAreIdempotentAndUseCatalogAmount()
    {
        using var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrder(shopper);
        var payBody = new PayOrderRequest(ValidCard(), null);

        var firstPay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", payBody);
        firstPay.EnsureSuccessStatusCode();
        var secondPay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", payBody);
        secondPay.EnsureSuccessStatusCode();
        Assert.AreEqual(1, _payPal.AuthorizeCalls);
        Assert.AreEqual(19.50m, _payPal.LastAuthorizedAmount);

        using var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();
        var capture = await fulfil.Content.ReadFromJsonAsync<OrderPaymentResponse>();
        Assert.AreEqual("Fulfilled", capture!.PaymentStatus);
        Assert.AreEqual(19.50m, capture.CapturedAmount);
        Assert.AreEqual(0.50m, capture.PayPalFee);
        Assert.AreEqual(19.00m, capture.NetProceeds);

        var refundRequest = new RefundOrderRequest("return-line-1", 5m);
        var firstRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        firstRefund.EnsureSuccessStatusCode();
        var refund = await firstRefund.Content.ReadFromJsonAsync<PaymentRefundResponse>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(refund!.RefundId));
        var repeatedRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        repeatedRefund.EnsureSuccessStatusCode();
        Assert.AreEqual(1, _payPal.RefundCalls);

        var overRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new RefundOrderRequest("too-much", 15m));
        Assert.AreEqual(HttpStatusCode.Conflict, overRefund.StatusCode);
    }

    [TestMethod]
    public async Task SavedCardIsShopperOwnedReusableAndUnavailableAfterDelete()
    {
        using var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var save = await shopper.PostAsJsonAsync("api/payment-methods", new SavePaymentMethodRequest(ValidCard()));
        save.EnsureSuccessStatusCode();
        var method = await save.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.IsTrue(method!.PaymentMethodId > 0);
        Assert.AreEqual("1111", method.LastDigits);

        var list = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("api/payment-methods");
        Assert.AreEqual(1, list!.Count);

        var orderId = await CreateOrder(shopper);
        var savedPay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay",
            new PayOrderRequest(null, method.PaymentMethodId));
        savedPay.EnsureSuccessStatusCode();
        Assert.AreEqual(_payPal.TokenId, _payPal.LastVaultId);

        using var otherShopper = Client(CreateToken("other-shopper@example.test"));
        var deleteOther = await otherShopper.DeleteAsync($"api/payment-methods/{method.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteOther.StatusCode);

        var delete = await shopper.DeleteAsync($"api/payment-methods/{method.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var afterDelete = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("api/payment-methods");
        Assert.AreEqual(0, afterDelete!.Count);

        var newOrderId = await CreateOrder(shopper);
        var removedPay = await shopper.PostAsJsonAsync($"api/orders/{newOrderId}/pay",
            new PayOrderRequest(null, method.PaymentMethodId));
        Assert.AreEqual(HttpStatusCode.NotFound, removedPay.StatusCode);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> CreateOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", ValidOrder());
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("orderId").GetInt32();
    }

    private static CreateOrderRequest ValidOrder() => new(new[] { new CreateOrderLine(1, 1) },
        new ApiAddress("1 Main St", "Austin", "TX", "US", "78701", "US"));

    private static CardInput ValidCard() => new("Test Shopper", "4111111111111111", "2035-12", "123",
        new ApiAddress("1 Main St", "Austin", "TX", "US", "78701", "US"));

    private static string CreateToken(string userName)
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName) };
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY));
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        private readonly HashSet<string> _activeTokens = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProviderRefund> _refunds = new(StringComparer.Ordinal);
        public string TokenId { get; } = "vault-token-1";
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public decimal LastAuthorizedAmount { get; private set; }
        public string? LastVaultId { get; private set; }

        public Task<ProviderAuthorization> AuthorizeAsync(int orderId, decimal amount, string currency,
            ProviderCardSource source, string createRequestId, string authorizeRequestId, CancellationToken ct)
        {
            AuthorizeCalls++;
            LastAuthorizedAmount = amount;
            LastVaultId = source.VaultId;
            return Task.FromResult(new ProviderAuthorization($"PAYPAL-ORDER-{orderId}", "COMPLETED",
                $"AUTH-{orderId}", "CREATED", amount, currency, DateTimeOffset.UtcNow.AddDays(3)));
        }

        public Task<ProviderAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
            Task.FromResult(new ProviderAuthorizationStatus(authorizationId, "CREATED", LastAuthorizedAmount,
                "USD", DateTimeOffset.UtcNow.AddDays(3)));

        public Task<ProviderAuthorizationStatus> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken ct) => Task.FromResult(
                new ProviderAuthorizationStatus(authorizationId + "-R", "CREATED", amount, currency,
                    DateTimeOffset.UtcNow.AddDays(3)));

        public Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken ct) => Task.FromResult(
                new ProviderCapture("CAPTURE-1", "COMPLETED", amount, currency, .50m, amount - .50m));

        public Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken ct) => Task.FromResult(
            new ProviderCapture(captureId, "COMPLETED", LastAuthorizedAmount, "USD", .50m,
                LastAuthorizedAmount - .50m));

        public Task<ProviderAuthorizationStatus> VoidAsync(string authorizationId, string requestId,
            CancellationToken ct) => Task.FromResult(
                new ProviderAuthorizationStatus(authorizationId, "VOIDED", LastAuthorizedAmount, "USD", null));

        public Task<ProviderRefund> RefundAsync(string captureId, decimal amount, string currency,
            bool fullRemainingRefund, string requestId, CancellationToken ct)
        {
            if (!_refunds.TryGetValue(requestId, out var refund))
            {
                RefundCalls++;
                refund = new ProviderRefund($"REFUND-{RefundCalls}", "COMPLETED", amount, currency);
                _refunds.Add(requestId, refund);
            }
            return Task.FromResult(refund);
        }

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken ct) =>
            Task.FromResult(_refunds.Values.Single(r => r.RefundId == refundId));

        public Task<ProviderPaymentMethod> SaveCardAsync(string shopperId, CardInput card, string requestId,
            CancellationToken ct)
        {
            _activeTokens.Add(TokenId);
            return Task.FromResult(new ProviderPaymentMethod(TokenId, "CUSTOMER-1", card.Name,
                "VISA", "1111", card.Expiry));
        }

        public Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderPaymentMethod>>(_activeTokens.Select(token =>
                new ProviderPaymentMethod(token, customerId, "Test Shopper", "VISA", "1111", "2035-12")).ToList());

        public Task DeleteCardAsync(string tokenId, CancellationToken ct)
        {
            _activeTokens.Remove(tokenId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderTransaction>>(Array.Empty<ProviderTransaction>());
    }
}
