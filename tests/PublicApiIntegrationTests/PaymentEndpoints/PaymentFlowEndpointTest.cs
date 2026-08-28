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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public sealed class PaymentFlowEndpointTest
{
    [TestMethod]
    public async Task FullFlowIsSecureScopedAndIdempotent()
    {
        var gateway = new FakePayPalGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["PayPal:ClientId"] = "test-client",
                    ["PayPal:ClientSecret"] = "test-secret",
                    ["PayPal:Environment"] = "Sandbox",
                    ["PayPal:Currency"] = "USD"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(gateway);
            });
        });
        using var client = factory.CreateClient();

        var unauthenticated = await client.PostAsJsonAsync("/api/orders",
            new PlaceOrderRequest([new OrderLineRequest(1, 2)]));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        var order = await CreateOrder(client);
        Assert.AreEqual(39m, order.Total);
        Assert.AreEqual("AwaitingPayment", order.PaymentStatus);

        var payment = new PayOrderRequest(
            new CardRequestDto("Test Shopper", "4111111111111111", "2030-12", "123",
                new BillingAddressRequestDto("US", "1 Main St", null, "San Jose", "CA", "95131")),
            null);
        var paidResponse = await client.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", payment);
        paidResponse.EnsureSuccessStatusCode();
        var paid = await paidResponse.Content.ReadFromJsonAsync<PaymentStateResponse>();
        Assert.AreEqual("Authorized", paid!.PaymentStatus);

        var replay = await client.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", payment);
        replay.EnsureSuccessStatusCode();
        Assert.AreEqual(1, gateway.AuthorizationCalls);

        var forbiddenFulfil = await client.PostAsync($"/api/orders/{order.OrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);

        Authenticate(client, ApiTokenHelper.GetAdminUserToken());
        var fulfilResponse = await client.PostAsync($"/api/orders/{order.OrderId}/fulfil", null);
        fulfilResponse.EnsureSuccessStatusCode();
        var fulfilled = await fulfilResponse.Content.ReadFromJsonAsync<PaymentStateResponse>();
        Assert.AreEqual("Captured", fulfilled!.PaymentStatus);
        Assert.AreEqual("Fulfilled", fulfilled.FulfilmentStatus);
        Assert.AreEqual(39m, fulfilled.CapturedAmount);
        Assert.AreEqual(1m, fulfilled.PayPalFee);
        Assert.AreEqual(38m, fulfilled.NetProceeds);

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        var refundRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/refunds")
        {
            Content = JsonContent.Create(new RefundOrderRequest(10m))
        };
        refundRequest.Headers.Add("Idempotency-Key", "refund-one");
        var refundResponse = await client.SendAsync(refundRequest);
        refundResponse.EnsureSuccessStatusCode();
        var refund = await refundResponse.Content.ReadFromJsonAsync<RefundResponse>();
        Assert.IsTrue(refund!.RefundId > 0);

        var repeatedRefundRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/refunds")
        {
            Content = JsonContent.Create(new RefundOrderRequest(10m))
        };
        repeatedRefundRequest.Headers.Add("Idempotency-Key", "refund-one");
        var repeatedRefundResponse = await client.SendAsync(repeatedRefundRequest);
        repeatedRefundResponse.EnsureSuccessStatusCode();
        var repeatedRefund = await repeatedRefundResponse.Content.ReadFromJsonAsync<RefundResponse>();
        Assert.AreEqual(refund.RefundId, repeatedRefund!.RefundId);
        Assert.AreEqual(1, gateway.RefundCalls);

        var saveResponse = await client.PostAsJsonAsync("/api/payment-methods",
            new SavePaymentMethodRequest(payment.Card!));
        saveResponse.EnsureSuccessStatusCode();
        var saved = await saveResponse.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.IsNotNull(saved);
        Assert.AreEqual("1111", saved.LastDigits);

        var secondOrder = await CreateOrder(client);
        var savedCardPayResponse = await client.PostAsJsonAsync($"/api/orders/{secondOrder.OrderId}/pay",
            new PayOrderRequest(null, saved.PaymentMethodId));
        savedCardPayResponse.EnsureSuccessStatusCode();
        var savedCardPayment = await savedCardPayResponse.Content.ReadFromJsonAsync<PaymentStateResponse>();
        Assert.AreNotEqual(paid.PayPalOrderId, savedCardPayment!.PayPalOrderId);
        Assert.AreEqual("vault-1", gateway.LastVaultIdUsed);

        Authenticate(client, ApiTokenHelper.GetAdminUserToken());
        var cancelResponse = await client.PostAsync($"/api/orders/{secondOrder.OrderId}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<PaymentStateResponse>();
        Assert.AreEqual("Cancelled", cancelled!.PaymentStatus);
        var reconciliation = await client.GetAsync(
            $"/api/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}");
        reconciliation.EnsureSuccessStatusCode();

        Authenticate(client, ApiTokenHelper.GetNormalUserToken());
        var deleteResponse = await client.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var methods = await client.GetFromJsonAsync<IReadOnlyList<PaymentMethodResponse>>("/api/payment-methods");
        Assert.AreEqual(0, methods!.Count);

        var thirdOrder = await CreateOrder(client);
        var deletedCardPayResponse = await client.PostAsJsonAsync($"/api/orders/{thirdOrder.OrderId}/pay",
            new PayOrderRequest(null, saved.PaymentMethodId));
        Assert.AreEqual(HttpStatusCode.NotFound, deletedCardPayResponse.StatusCode);

        Authenticate(client, ApiTokenHelper.GetAdminUserToken());
        var crossOwnerRefund = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/refunds")
        {
            Content = JsonContent.Create(new RefundOrderRequest(1m))
        };
        crossOwnerRefund.Headers.Add("Idempotency-Key", "wrong-owner");
        var crossOwnerResponse = await client.SendAsync(crossOwnerRefund);
        Assert.AreEqual(HttpStatusCode.NotFound, crossOwnerResponse.StatusCode);
    }

    private static async Task<PlaceOrderResponse> CreateOrder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/orders",
            new PlaceOrderRequest([new OrderLineRequest(1, 2)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        public int AuthorizationCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastVaultIdUsed { get; private set; }

        public Task<PayPalAuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, string currency,
            string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
            CancellationToken cancellationToken)
        {
            AuthorizationCalls++;
            LastVaultIdUsed = vaultId;
            return Task.FromResult(new PayPalAuthorizationResult($"paypal-order-{orderReference}", "COMPLETED",
                $"auth-{orderReference}", "CREATED", amount, currency, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(29), false));
        }

        public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(new PayPalAuthorizationResult(
            string.Empty, null, authorizationId, "CREATED", 39m, "USD", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29), false));

        public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            GetAuthorizationAsync(authorizationId, cancellationToken);

        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken) => Task.FromResult(new PayPalCaptureResult(
            authorizationId.Replace("auth", "capture"), "COMPLETED", amount, currency, 1m, amount - 1m,
            DateTimeOffset.UtcNow));

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCaptureResult(captureId, "COMPLETED", 39m, "USD", 1m, 38m,
                DateTimeOffset.UtcNow));

        public Task<string?> VoidAsync(string authorizationId, string requestId,
            CancellationToken cancellationToken) => Task.FromResult<string?>("VOIDED");

        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
            string idempotencyKey, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefundResult($"refund-{idempotencyKey}", "COMPLETED", amount,
                currency, DateTimeOffset.UtcNow));
        }

        public Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalRefundResult(refundId, "COMPLETED", 10m, "USD", DateTimeOffset.UtcNow));

        public Task<PayPalVaultResult> SaveCardAsync(string buyerId, CardInput card, string requestId,
            CancellationToken cancellationToken) => Task.FromResult(new PayPalVaultResult(
            "vault-1", card.Name, "VISA", "1111", card.Expiry, "CREDIT"));

        public Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransaction>>([]);
    }
}
