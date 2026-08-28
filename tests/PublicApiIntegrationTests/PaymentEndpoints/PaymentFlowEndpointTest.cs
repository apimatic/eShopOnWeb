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
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowEndpointTest
{
    [TestMethod]
    public async Task DrivesAuthorizationCaptureRefundVaultReuseAndOwnershipThroughApi()
    {
        var payPal = new FakePayPalClient();
        await using var factory = new PaymentApiFactory(payPal);
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var otherUser = factory.CreateClient();
        otherUser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetUserToken("somebody-else@example.com"));

        var card = Card();
        var firstOrder = await PlaceOrderAsync(user);
        var payResponse = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/pay",
            new PayOrderRequest(card, null));
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode,
            await payResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("Authorized", (await payResponse.Content.ReadFromJsonAsync<OrderResponse>())!.Payment.State);

        var duplicatePay = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/pay",
            new PayOrderRequest(card, null));
        Assert.AreEqual(HttpStatusCode.OK, duplicatePay.StatusCode);
        Assert.AreEqual(1, payPal.AuthorizeCalls, "A retry must not authorize twice.");

        var forbiddenFulfil = await user.PostAsync($"/api/orders/{firstOrder}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);
        var fulfil = await admin.PostAsync($"/api/orders/{firstOrder}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();
        var fulfilled = (await fulfil.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.AreEqual("Fulfilled", fulfilled.Payment.State);
        Assert.AreEqual(0.30m, fulfilled.Payment.PayPalFee);
        Assert.AreEqual(fulfilled.Payment.CapturedAmount - 0.30m, fulfilled.Payment.NetProceeds);

        var refund = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/refunds",
            new RefundOrderRequest(2.00m, "return-line-1"));
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
        var refundBody = (await refund.Content.ReadFromJsonAsync<RefundOrderResponse>())!;
        Assert.IsTrue(refundBody.RefundId > 0);
        var duplicateRefund = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/refunds",
            new RefundOrderRequest(2.00m, "return-line-1"));
        Assert.AreEqual(HttpStatusCode.Created, duplicateRefund.StatusCode);
        Assert.AreEqual(1, payPal.RefundCalls, "A repeated refund key must not refund twice.");

        var secondRefund = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/refunds",
            new RefundOrderRequest(1.00m, "return-line-2"));
        Assert.AreEqual(HttpStatusCode.Created, secondRefund.StatusCode);
        Assert.AreEqual(2, payPal.RefundCalls, "Distinct partial refund keys must remain valid.");
        var overRefund = await user.PostAsJsonAsync($"/api/orders/{firstOrder}/refunds",
            new RefundOrderRequest(99m, "too-much"));
        Assert.AreEqual(HttpStatusCode.BadRequest, overRefund.StatusCode);

        var save = await user.PostAsJsonAsync("/api/payment-methods",
            new SavePaymentMethodRequest(card));
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saved = (await save.Content.ReadFromJsonAsync<SavePaymentMethodResponse>())!;
        Assert.IsTrue(saved.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.Last4);

        var otherList = await otherUser.GetFromJsonAsync<List<PaymentMethodResponse>>(
            "/api/payment-methods");
        Assert.AreEqual(0, otherList!.Count);
        var otherDelete = await otherUser.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);

        var secondOrder = await PlaceOrderAsync(user);
        var savedPay = await user.PostAsJsonAsync($"/api/orders/{secondOrder}/pay",
            new PayOrderRequest(null, saved.PaymentMethodId));
        savedPay.EnsureSuccessStatusCode();
        Assert.AreEqual("vault-token", payPal.LastVaultId);

        var hiddenOrder = await otherUser.PostAsJsonAsync($"/api/orders/{secondOrder}/pay",
            new PayOrderRequest(card, null));
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenOrder.StatusCode);

        var delete = await user.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var methods = await user.GetFromJsonAsync<List<PaymentMethodResponse>>("/api/payment-methods");
        Assert.AreEqual(0, methods!.Count);

        var thirdOrder = await PlaceOrderAsync(user);
        var deletedMethodPay = await user.PostAsJsonAsync($"/api/orders/{thirdOrder}/pay",
            new PayOrderRequest(null, saved.PaymentMethodId));
        Assert.AreEqual(HttpStatusCode.NotFound, deletedMethodPay.StatusCode);
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new PlaceOrderRequest(
            new[] { new PlaceOrderItemRequest(1, 1) },
            new ShippingAddressRequest("1 Main St", "Austin", "TX", "US", "78701")));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId;
    }

    private static CardInput Card() => new("Sandbox Shopper", "4111" + new string('1', 12),
        DateTime.UtcNow.AddYears(2).ToString("yyyy-MM"), "123",
        new BillingAddressInput("1 Main St", null, "Austin", "TX", "78701", "US"));

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        private readonly IPayPalClient _payPal;
        public PaymentApiFactory(IPayPalClient payPal) => _payPal = payPal;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PayPal:Currency", "USD");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton(_payPal);
            });
        }
    }

    private sealed class FakePayPalClient : IPayPalClient
    {
        private int _authorization;
        private int _capture;
        private int _refund;
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastVaultId { get; private set; }

        public Task<PayPalAuthorization> AuthorizeOrderAsync(string paymentReference, decimal amount,
            string currency, CardInput? card, string? vaultId, string requestId,
            CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastVaultId = vaultId;
            var id = Interlocked.Increment(ref _authorization);
            return Task.FromResult(new PayPalAuthorization($"ORDER-{id}", "COMPLETED", $"AUTH-{id}",
                "CREATED", amount, currency, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(29), false));
        }

        public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalAuthorization(string.Empty, "COMPLETED", authorizationId + "-R",
                "CREATED", amount, currency, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(29), false));

        public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
            string currency, string invoiceId, string requestId, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _capture);
            return Task.FromResult(new PayPalCapture($"CAP-{id}", "COMPLETED", amount, currency,
                0.30m, amount - 0.30m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCapture> GetCaptureAsync(string captureId,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<string> VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.FromResult("VOIDED");

        public Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
            string requestId, string customId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            var id = Interlocked.Increment(ref _refund);
            return Task.FromResult(new PayPalRefund($"REF-{id}", "COMPLETED", amount, currency,
                DateTimeOffset.UtcNow));
        }

        public Task<PayPalRefund> GetRefundAsync(string refundId,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<PayPalPaymentToken> CreatePaymentTokenAsync(string customerId, CardInput card,
            string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalPaymentToken("vault-token", "VISA", "1111", card.Expiry));

        public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
