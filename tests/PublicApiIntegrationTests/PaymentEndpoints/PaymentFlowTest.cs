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
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTest
{
    [TestMethod]
    public async Task CardAuthorizationCaptureRefundAndVaultReuseAreSeparateAndIdempotent()
    {
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient, FakePayPalClient>();
            }));
        var shopper = application.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var admin = application.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var card = ValidCard();

        var savedResponse = await shopper.PostAsJsonAsync("api/payment-methods",
            new SavePaymentMethodRequest { Card = card });
        Assert.AreEqual(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.IsNotNull(saved);
        Assert.AreEqual("1111", saved.LastDigits);

        var order = await CreateOrder(shopper, 1);
        var paidResponse = await shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay",
            new PayOrderRequest { Card = card });
        paidResponse.EnsureSuccessStatusCode();
        var paid = await paidResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.AreEqual("Authorized", paid!.PaymentStatus);

        var duplicatePay = await shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/pay",
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        var duplicatePaid = await duplicatePay.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.AreEqual(paid.AuthorizationId, duplicatePaid!.AuthorizationId);

        var shopperFulfil = await shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperFulfil.StatusCode);

        var fulfilledResponse = await admin.PostAsJsonAsync($"api/orders/{order.OrderId}/fulfil", new { });
        fulfilledResponse.EnsureSuccessStatusCode();
        var fulfilled = await fulfilledResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.AreEqual("Captured", fulfilled!.PaymentStatus);
        Assert.AreEqual(fulfilled.Total, fulfilled.CapturedAmount);
        Assert.AreEqual(0.50m, fulfilled.PayPalFee);

        var refundRequest = new RefundOrderRequest { Amount = 1m, IdempotencyKey = "same-refund" };
        var refundOne = await (await shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/refunds", refundRequest))
            .Content.ReadFromJsonAsync<RefundResponse>();
        var refundAgain = await (await shopper.PostAsJsonAsync($"api/orders/{order.OrderId}/refunds", refundRequest))
            .Content.ReadFromJsonAsync<RefundResponse>();
        Assert.AreEqual(refundOne!.RefundId, refundAgain!.RefundId);

        var secondOrder = await CreateOrder(shopper, 2);
        var savedCardPayment = await shopper.PostAsJsonAsync($"api/orders/{secondOrder.OrderId}/pay",
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        savedCardPayment.EnsureSuccessStatusCode();
        Assert.AreEqual("Authorized",
            (await savedCardPayment.Content.ReadFromJsonAsync<OrderResponse>())!.PaymentStatus);
    }

    private static async Task<OrderResponse> CreateOrder(HttpClient client, int catalogItemId)
    {
        var response = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest
        {
            Items = new List<CreateOrderLineRequest>
            {
                new() { CatalogItemId = catalogItemId, Quantity = 1 }
            },
            ShippingAddress = new ShippingAddressRequest
            {
                Street = "1 Main St", City = "Seattle", State = "WA", Country = "US", ZipCode = "98101"
            }
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static CardDetails ValidCard() => new()
    {
        Name = "Test Shopper", Number = "9" + new string('0', 12), Expiry = "2030-12", SecurityCode = "123",
        BillingAddress = new CardBillingAddress
        {
            AddressLine1 = "1 Main St", City = "Seattle", State = "WA", PostalCode = "98101", CountryCode = "US"
        }
    };

    private sealed class FakePayPalClient : IPayPalClient
    {
        private int _order;
        private int _refund;
        public Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalOrderResult($"ORDER-{++_order}", "CREATED"));
        public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, CardDetails? card,
            string? vaultId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalAuthorizationResult(payPalOrderId, "COMPLETED", $"AUTH-{payPalOrderId}",
                "CREATED", payPalOrderId.EndsWith("1", StringComparison.Ordinal) ? 19.5m : 8.5m, "USD",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        public Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(new PayPalAuthorizationDetails(
                authorizationId, "CREATED", 19.5m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        public Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalAuthorizationDetails(authorizationId + "-R", "CREATED", amount,
                currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
            string invoiceId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCaptureResult("CAPTURE-1", "COMPLETED", amount, currency, 0.50m,
                amount - 0.50m, DateTimeOffset.UtcNow));
        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCaptureResult(captureId, "COMPLETED", 19.5m, "USD", 0.50m,
                19m, DateTimeOffset.UtcNow));
        public Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
            string customId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalRefundResult($"REFUND-{++_refund}", "COMPLETED", amount, currency,
                0m, amount, DateTimeOffset.UtcNow));
        public Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string merchantCustomerId,
            CardDetails card, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalPaymentTokenResult("TOKEN-1", "CUSTOMER-1", "VISA", "1111", "2030-12"));
        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
            int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalTransactionPage(Array.Empty<PayPalTransaction>(), page, page));
    }
}
