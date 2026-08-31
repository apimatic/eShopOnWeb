using System;
using System.Collections.Generic;
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

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentFlowTests
{
    [TestMethod]
    public async Task SupportsAuthorizeCaptureRefundVaultReuseAndOwnership()
    {
        var fakePayPal = new FakePayPalClient();
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(fakePayPal);
            }));
        var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        var admin = application.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var firstOrder = await PlaceOrder(shopper);
        Assert.IsTrue(firstOrder.OrderId > 0);

        var forbiddenFulfil = await shopper.PostAsync($"api/orders/{firstOrder.OrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);

        var otherShopper = application.CreateClient();
        otherShopper.DefaultRequestHeaders.Authorization = Bearer(CreateOtherShopperToken());
        var hiddenOrder = await otherShopper.PostAsJsonAsync($"api/orders/{firstOrder.OrderId}/pay",
            new PayOrderRequest(TestCard(), null));
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenOrder.StatusCode);

        var paid = await Post<PayOrderRequest, OrderActionResponse>(shopper,
            $"api/orders/{firstOrder.OrderId}/pay", new PayOrderRequest(TestCard(), null));
        Assert.AreEqual("Authorized", paid.PaymentStatus);
        Assert.AreEqual(firstOrder.Total, fakePayPal.LastAuthorizedAmount);

        var fulfilled = await Post<object, OrderActionResponse>(admin,
            $"api/orders/{firstOrder.OrderId}/fulfil", new { });
        Assert.AreEqual("Fulfilled", fulfilled.FulfilmentStatus);
        Assert.AreEqual(firstOrder.Total, fulfilled.CapturedAmount);
        Assert.AreEqual(0.30m, fulfilled.PaypalFee);

        var refundRequest = new RefundOrderRequest(firstOrder.Total / 2, "refund-test-key");
        var refund = await Post<RefundOrderRequest, RefundCreatedResponse>(shopper,
            $"api/orders/{firstOrder.OrderId}/refunds", refundRequest);
        var repeatedRefund = await Post<RefundOrderRequest, RefundCreatedResponse>(shopper,
            $"api/orders/{firstOrder.OrderId}/refunds", refundRequest);
        Assert.AreEqual(refund.RefundId, repeatedRefund.RefundId);
        Assert.AreEqual(1, fakePayPal.RefundCallCount);

        var saved = await Post<SavePaymentMethodRequest, PaymentMethodResponse>(shopper,
            "api/payment-methods", new SavePaymentMethodRequest(TestCard()));
        Assert.IsTrue(saved.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.LastDigits);

        var secondOrder = await PlaceOrder(shopper);
        var savedCardPayment = await Post<PayOrderRequest, OrderActionResponse>(shopper,
            $"api/orders/{secondOrder.OrderId}/pay", new PayOrderRequest(null, saved.PaymentMethodId));
        Assert.AreEqual("Authorized", savedCardPayment.PaymentStatus);
        Assert.IsTrue(fakePayPal.UsedVaultId);

        var delete = await shopper.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var methods = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("api/payment-methods");
        Assert.AreEqual(0, methods!.Count);

        var myOrders = await shopper.GetFromJsonAsync<List<MyOrderResponse>>("api/my-orders");
        Assert.IsTrue(myOrders!.Count >= 2);

        var reconciliationForbidden = await shopper.GetAsync(
            $"api/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliationForbidden.StatusCode);
    }

    private static async Task<OrderCreatedResponse> PlaceOrder(HttpClient client) =>
        await Post<PlaceOrderRequest, OrderCreatedResponse>(client, "api/orders",
            new PlaceOrderRequest(new[] { new OrderLineRequest(1, 1) },
                new AddressRequest("1 Main St", "Boston", "MA", "US", "02101")));

    private static CardRequest TestCard() => new("Test Shopper", "4111111111111111", "2035-12",
        "123", new BillingAddressRequest("US", "1 Main St", null, "Boston", "MA", "02101"));

    private static async Task<TResponse> Post<TRequest, TResponse>(HttpClient client, string uri,
        TRequest request)
    {
        var response = await client.PostAsJsonAsync(uri, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static string CreateOtherShopperToken()
    {
        // Any valid non-admin JWT is enough; replacing the seeded username in this test token
        // keeps the ownership assertion independent of Identity storage.
        var original = ApiTokenHelper.GetNormalUserToken();
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(original);
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(
            Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY));
        return handler.WriteToken(handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "other@microsoft.com")
            }),
            Expires = parsed.ValidTo,
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        }));
    }

    private sealed class FakePayPalClient : IPayPalClient
    {
        private int _sequence;
        private readonly Dictionary<string, decimal> _orderAmounts = new();
        private readonly Dictionary<string, PayPalAuthorizationResult> _authorizations = new();
        private readonly Dictionary<string, PayPalCaptureResult> _captures = new();
        public decimal LastAuthorizedAmount { get; private set; }
        public bool UsedVaultId { get; private set; }
        public int RefundCallCount { get; private set; }

        public Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            var id = $"ORDER-{orderId}";
            _orderAmounts[id] = amount;
            return Task.FromResult(new PayPalOrderResult(id, "CREATED"));
        }

        public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, CardInput? card,
            string? vaultId, string requestId, CancellationToken cancellationToken)
        {
            UsedVaultId |= vaultId is not null;
            LastAuthorizedAmount = _orderAmounts[paypalOrderId];
            var id = $"AUTH-{++_sequence}";
            var result = new PayPalAuthorizationResult(id, "CREATED", LastAuthorizedAmount, "USD",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
            _authorizations[id] = result;
            LastAuthorizedAmount = result.Amount;
            return Task.FromResult(result);
        }

        public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(_authorizations[authorizationId]);

        public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(_authorizations[authorizationId]);

        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, string paymentReference, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken)
        {
            var result = new PayPalCaptureResult($"CAP-{orderId}", "COMPLETED", amount, currency,
                0.30m, amount - 0.30m, DateTimeOffset.UtcNow);
            _captures[result.Id] = result;
            return Task.FromResult(result);
        }

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
            CancellationToken cancellationToken) => Task.FromResult(_captures[captureId]);

        public Task VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            RefundCallCount++;
            return Task.FromResult(new PayPalRefundResult($"REF-{RefundCallCount}", "COMPLETED",
                amount, currency, DateTimeOffset.UtcNow));
        }

        public Task<PayPalPaymentTokenResult> SaveCardAsync(string buyerId, CardInput card,
            string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalPaymentTokenResult("TOKEN-1", "VISA", "1111", "2035-12"));

        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransactionRecord>>(Array.Empty<PayPalTransactionRecord>());
    }
}
