using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private PaymentApiFactory _factory = null!;
    private HttpClient _shopper = null!;
    private HttpClient _admin = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new PaymentApiFactory();
        _shopper = Client(ApiTokenHelper.GetNormalUserToken());
        _admin = Client(ApiTokenHelper.GetAdminUserToken());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _shopper.Dispose();
        _admin.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task SupportsAuthorizationCaptureRefundAndIdempotentRetries()
    {
        var order = await PlaceOrder();
        var pay = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay", new PayOrderRequest { Card = Card() });
        pay.EnsureSuccessStatusCode();

        var repeatedPay = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay", new PayOrderRequest { Card = Card() });
        repeatedPay.EnsureSuccessStatusCode();
        Assert.AreEqual(1, _factory.PayPal.AuthorizationCalls);

        var shopperFulfil = await _shopper.PostAsync($"api/orders/{order.OrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperFulfil.StatusCode);

        var fulfil = await _admin.PostAsync($"api/orders/{order.OrderId}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();
        var captured = await fulfil.Content.ReadFromJsonAsync<FulfilOrderResponse>();
        Assert.AreEqual(order.Total, captured!.CapturedAmount);
        Assert.AreEqual(0.50m, captured.PayPalFee);
        Assert.AreEqual(order.Total - 0.50m, captured.NetProceeds);

        const string refundKey = "return-1";
        var refund = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/refunds", new RefundOrderRequest
        {
            Amount = 2.00m,
            IdempotencyKey = refundKey
        });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
        var refundResult = await refund.Content.ReadFromJsonAsync<RefundOrderResponse>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(refundResult!.RefundId));

        var repeatedRefund = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/refunds", new RefundOrderRequest
        {
            Amount = 2.00m,
            IdempotencyKey = refundKey
        });
        Assert.AreEqual(HttpStatusCode.Created, repeatedRefund.StatusCode);
        Assert.AreEqual(1, _factory.PayPal.RefundCalls);

        var tooLarge = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/refunds", new RefundOrderRequest
        {
            Amount = order.Total,
            IdempotencyKey = "return-too-large"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, tooLarge.StatusCode);
    }

    [TestMethod]
    public async Task SavedCardIsOwnerScopedReusableAndNotUsableAfterDeletion()
    {
        var save = await _shopper.PostAsJsonAsync("api/payment-methods", Card());
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.AreEqual("1111", saved!.Last4);

        var methods = await _shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("api/payment-methods");
        Assert.IsTrue(methods!.Any(x => x.PaymentMethodId == saved.PaymentMethodId));

        var order = await PlaceOrder();
        var pay = await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay", new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        pay.EnsureSuccessStatusCode();
        Assert.AreEqual("vault-1", _factory.PayPal.LastVaultId);

        var otherUsersOrder = await PlaceOrder(_admin);
        var crossUser = await _admin.PostAsJsonAsync($"api/orders/{otherUsersOrder.OrderId}/pay", new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, crossUser.StatusCode);

        var delete = await _shopper.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        methods = await _shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("api/payment-methods");
        Assert.IsFalse(methods!.Any());

        var unpaidOrder = await PlaceOrder();
        var deletedUse = await _shopper.PostAsJsonAsync($"api/orders/{unpaidOrder.OrderId}/pay", new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, deletedUse.StatusCode);
    }

    [TestMethod]
    public async Task CancelAndReconciliationRequireOperatorRole()
    {
        var order = await PlaceOrder();
        (await _shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay", new PayOrderRequest { Card = Card() })).EnsureSuccessStatusCode();

        var denied = await _shopper.PostAsync($"api/orders/{order.OrderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        var cancelled = await _admin.PostAsync($"api/orders/{order.OrderId}/cancel", null);
        cancelled.EnsureSuccessStatusCode();
        Assert.AreEqual(1, _factory.PayPal.VoidCalls);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _shopper.GetAsync($"api/reconciliation?from={from}&to={to}")).StatusCode);
        var report = await _admin.GetAsync($"api/reconciliation?from={from}&to={to}");
        report.EnsureSuccessStatusCode();
    }

    private async Task<CreateOrderResponse> PlaceOrder(HttpClient? client = null)
    {
        var response = await (client ?? _shopper).PostAsJsonAsync("api/orders", new PlaceOrderRequest
        {
            Items = new List<OrderLineRequest> { new() { CatalogItemId = 1, Quantity = 1 } },
            ShippingAddress = new ShippingAddressRequest
            {
                Street = "1 Main St",
                City = "San Jose",
                State = "CA",
                Country = "US",
                ZipCode = "95131"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static SavePaymentMethodRequest Card() => new()
    {
        Number = "4111 1111 1111 1111",
        Expiry = "2030-12",
        SecurityCode = "123",
        Name = "Test Shopper",
        AddressLine1 = "1 Main St",
        City = "San Jose",
        State = "CA",
        PostalCode = "95131",
        CountryCode = "US"
    };

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        public FakePayPalClient PayPal { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("UseOnlyInMemoryDatabase", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(PayPal);
            });
        }
    }

    private sealed class FakePayPalClient : IPayPalClient
    {
        private readonly ConcurrentDictionary<string, PayPalRefund> _refunds = new();
        public string Currency => "USD";
        public int AuthorizationCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int VoidCalls { get; private set; }
        public string? LastVaultId { get; private set; }

        public Task<PayPalSavedCard> SaveCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalSavedCard("vault-1", customerId ?? "customer-1", "VISA", "1111", card.Expiry, card.Name));

        public Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalAuthorization> AuthorizeAsync(int orderId, Guid paymentReference, decimal amount, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken)
        {
            AuthorizationCalls++;
            LastVaultId = vaultId;
            return Task.FromResult(new PayPalAuthorization($"paypal-order-{orderId}", $"authorization-{orderId}", "CREATED", amount, Currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        }

        public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalAuthorization(string.Empty, authorizationId + "-renewed", "CREATED", 0, Currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));

        public Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCapture("capture-" + authorizationId, "COMPLETED", amount, Currency, 0.50m, amount - 0.50m, DateTimeOffset.UtcNow));

        public Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
        {
            VoidCalls++;
            return Task.CompletedTask;
        }

        public Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string requestId, CancellationToken cancellationToken)
        {
            var value = _refunds.GetOrAdd(requestId, _ =>
            {
                RefundCalls++;
                return new PayPalRefund("refund-" + requestId, "COMPLETED", amount, Currency, DateTimeOffset.UtcNow);
            });
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
