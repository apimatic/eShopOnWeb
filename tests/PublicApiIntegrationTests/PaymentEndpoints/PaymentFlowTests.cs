using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    [TestMethod]
    public async Task AuthorizeCaptureAndRefundAreIdempotent()
    {
        var payPal = new FakePayPalClient();
        using var factory = CreateFactory(payPal);
        using var shopper = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        using var admin = AuthenticatedClient(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrder(shopper, 5);
        var payResponse = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new
        {
            card = Card()
        });
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        var secondPay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new
        {
            card = Card()
        });
        Assert.AreEqual(HttpStatusCode.OK, secondPay.StatusCode);
        Assert.AreEqual(1, payPal.AuthorizeCalls);

        payPal.AuthorizationStatus = "CAPTURED";
        var fulfilResponse = await admin.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode);
        Assert.AreEqual(1, payPal.CaptureCalls);
        using (var fulfilled = JsonDocument.Parse(await fulfilResponse.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual("Fulfilled", fulfilled.RootElement.GetProperty("status").GetString());
            Assert.AreEqual(0.50m,
                fulfilled.RootElement.GetProperty("payment").GetProperty("payPalFee").GetDecimal());
        }

        var refundRequest = new { amount = 3.25m, idempotencyKey = "partial-1" };
        var refund1 = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        var refund2 = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        Assert.AreEqual(HttpStatusCode.Created, refund1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, refund2.StatusCode);
        Assert.AreEqual(1, payPal.RefundCalls);

        using var firstJson = JsonDocument.Parse(await refund1.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await refund2.Content.ReadAsStringAsync());
        Assert.AreEqual(firstJson.RootElement.GetProperty("refundId").GetString(),
            secondJson.RootElement.GetProperty("refundId").GetString());

        var overRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new
        {
            amount = 6m,
            idempotencyKey = "partial-2"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, overRefund.StatusCode);
    }

    [TestMethod]
    public async Task SavedMethodIsPrivateAndCannotBeUsedAfterDeletion()
    {
        var payPal = new FakePayPalClient();
        using var factory = CreateFactory(payPal);
        using var shopper = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        using var otherUser = AuthenticatedClient(factory, ApiTokenHelper.GetAdminUserToken());

        var save = await shopper.PostAsJsonAsync("api/payment-methods", new { card = Card() });
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        using var savedJson = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        var paymentMethodId = savedJson.RootElement.GetProperty("paymentMethodId").GetInt32();
        var paymentMethod = savedJson.RootElement.GetProperty("paymentMethod");
        Assert.AreEqual("1111", paymentMethod.GetProperty("lastDigits").GetString());
        Assert.IsFalse(paymentMethod.TryGetProperty("number", out _));

        var otherList = await otherUser.GetFromJsonAsync<JsonElement[]>("api/payment-methods");
        Assert.AreEqual(0, otherList!.Length);
        var otherDelete = await otherUser.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);

        var orderId = await CreateOrder(shopper, 4);
        var paid = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay",
            new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.OK, paid.StatusCode);
        Assert.AreEqual("vault-token", payPal.LastVaultId);

        var deleted = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        var nextOrderId = await CreateOrder(shopper, 4);
        var useDeleted = await shopper.PostAsJsonAsync($"api/orders/{nextOrderId}/pay",
            new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, useDeleted.StatusCode);
    }

    [TestMethod]
    public async Task OperatorRoutesRejectShopperToken()
    {
        using var factory = CreateFactory(new FakePayPalClient());
        using var shopper = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrder(shopper, 5);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync($"api/orders/{orderId}/cancel", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-02T00:00:00Z")).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakePayPalClient payPal) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(payPal);
            }));

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory,
        string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> CreateOrder(HttpClient client, int catalogItemId)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId, quantity = 1 } },
            shippingAddress = new
            {
                street = "123 Main Street",
                city = "San Jose",
                state = "CA",
                country = "US",
                zipCode = "95131"
            }
        });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("orderId").GetInt32();
    }

    private static object Card() => new
    {
        name = "Test Shopper",
        number = "4111111111111111",
        expiry = "2030-12",
        securityCode = "123",
        billingAddress = new
        {
            addressLine1 = "123 Main Street",
            city = "San Jose",
            state = "CA",
            postalCode = "95131",
            countryCode = "US"
        }
    };

    private sealed class FakePayPalClient : IPayPalClient
    {
        private readonly ConcurrentDictionary<string, PayPalRefund> _refunds = new();
        private decimal _lastAmount;
        public string Currency => "USD";
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastVaultId { get; private set; }
        public string AuthorizationStatus { get; set; } = "CREATED";

        public Task<string> CreateOrderAsync(decimal amount, string paymentReference,
            string requestId, CancellationToken cancellationToken)
        {
            _lastAmount = amount;
            return Task.FromResult("PAYPAL-ORDER");
        }

        public Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId, CardInput? card,
            string? vaultId, string requestId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastVaultId = vaultId;
            return Task.FromResult(Authorization(payPalOrderId));
        }

        public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
            string payPalOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(Authorization(payPalOrderId));

        public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId,
            string payPalOrderId, decimal amount, string requestId,
            CancellationToken cancellationToken) => Task.FromResult(Authorization(payPalOrderId));

        public Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
            string paymentReference, string requestId, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCapture("CAPTURE", "COMPLETED", amount, Currency,
                0.50m, amount - 0.50m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCapture> GetCaptureAsync(string captureId,
            CancellationToken cancellationToken) => Task.FromResult(
            new PayPalCapture(captureId, "COMPLETED", 8.50m, Currency, 0.50m, 8m,
                DateTimeOffset.UtcNow));

        public Task VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalRefund> RefundAsync(string captureId, decimal amount,
            string paymentReference, string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            var refund = new PayPalRefund($"REFUND-{RefundCalls}", "COMPLETED", amount,
                Currency, DateTimeOffset.UtcNow);
            _refunds[refund.RefundId] = refund;
            return Task.FromResult(refund);
        }

        public Task<PayPalRefund> GetRefundAsync(string refundId,
            CancellationToken cancellationToken) => Task.FromResult(_refunds[refundId]);

        public Task<PayPalSavedCard> SaveCardAsync(string merchantCustomerId, CardInput card,
            string requestId, CancellationToken cancellationToken) => Task.FromResult(
            new PayPalSavedCard("vault-token", "customer-id", "VISA", "1111", "2030-12"));

        public Task DeletePaymentTokenAsync(string vaultId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, int page, CancellationToken cancellationToken) => Task.FromResult(
            new PayPalTransactionPage(Array.Empty<PayPalTransaction>(), page, 1));

        private PayPalAuthorization Authorization(string payPalOrderId) => new(
            payPalOrderId, "COMPLETED", "AUTHORIZATION", AuthorizationStatus, _lastAmount, "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
    }
}
