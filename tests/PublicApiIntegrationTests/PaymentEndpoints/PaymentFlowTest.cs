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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTest
{
    [TestMethod]
    public async Task StaleAuthorizationIsRenewedAndCancellationVoidsTheHold()
    {
        var gateway = new FakePayPalGateway { AuthorizationAge = TimeSpan.FromDays(4) };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var staleOrder = await CreateOrderAsync(client, 2);
        await PostAsync<PayOrderResponse>(client, $"api/orders/{staleOrder.OrderId}/pay",
            new PayOrderRequest { Card = TestCard() });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var fulfilled = await PostAsync<FulfilOrderResponse>(client,
            $"api/orders/{staleOrder.OrderId}/fulfil", new { });
        Assert.AreEqual("Fulfilled", fulfilled.Order.FulfillmentStatus);
        Assert.AreEqual(1, gateway.ReauthorizeCalls);

        gateway.AuthorizationAge = TimeSpan.Zero;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var cancelOrder = await CreateOrderAsync(client, 3);
        await PostAsync<PayOrderResponse>(client, $"api/orders/{cancelOrder.OrderId}/pay",
            new PayOrderRequest { Card = TestCard() });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var cancelled = await PostAsync<CancelOrderResponse>(client,
            $"api/orders/{cancelOrder.OrderId}/cancel", new { });
        Assert.AreEqual("Cancelled", cancelled.Order.FulfillmentStatus);
        Assert.AreEqual("Voided", cancelled.Order.Payment!.State);
        Assert.AreEqual(1, gateway.VoidCalls);
    }

    [TestMethod]
    public async Task CompletePaymentAndSavedCardFlowIsScopedAndIdempotent()
    {
        var gateway = new FakePayPalGateway();
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var order = await CreateOrderAsync(client, 2);
        var card = TestCard();
        var paid = await PostAsync<PayOrderResponse>(client, $"api/orders/{order.OrderId}/pay",
            new PayOrderRequest { Card = card });
        var paidAgain = await PostAsync<PayOrderResponse>(client, $"api/orders/{order.OrderId}/pay",
            new PayOrderRequest { Card = card });

        Assert.AreEqual("Authorized", paid.Order.Payment!.State);
        Assert.AreEqual(paid.Order.Payment.AuthorizationId, paidAgain.Order.Payment!.AuthorizationId);
        Assert.AreEqual(1, gateway.AuthorizeCalls);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var fulfilled = await PostAsync<FulfilOrderResponse>(client,
            $"api/orders/{order.OrderId}/fulfil", new { });
        var fulfilledAgain = await PostAsync<FulfilOrderResponse>(client,
            $"api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual("Fulfilled", fulfilled.Order.FulfillmentStatus);
        Assert.AreEqual(fulfilled.Order.Payment!.CaptureId, fulfilledAgain.Order.Payment!.CaptureId);
        Assert.AreEqual(1, gateway.CaptureCalls);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var refundRequest = new RefundOrderRequest { IdempotencyKey = "same-key", Amount = 1m };
        var refund = await PostAsync<RefundOrderResponse>(client,
            $"api/orders/{order.OrderId}/refunds", refundRequest);
        var refundAgain = await PostAsync<RefundOrderResponse>(client,
            $"api/orders/{order.OrderId}/refunds", refundRequest);
        Assert.AreEqual(refund.RefundId, refundAgain.RefundId);
        Assert.AreEqual(1, gateway.RefundCalls);

        var saved = await PostAsync<SavePaymentMethodResponse>(client, "api/payment-methods",
            new SavePaymentMethodRequest { Card = card });
        Assert.AreEqual("1111", saved.PaymentMethod.LastDigits);
        var secondOrder = await CreateOrderAsync(client, 3);
        var savedCardPayment = await PostAsync<PayOrderResponse>(client,
            $"api/orders/{secondOrder.OrderId}/pay",
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual("Authorized", savedCardPayment.Order.Payment!.State);
        Assert.AreEqual("VAULT-1", gateway.LastVaultIdUsed);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var crossShopperDelete = await client.DeleteAsync(
            $"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossShopperDelete.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var ownerDelete = await client.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        var methods = await client.GetFromJsonAsync<PaymentMethodsResponse>("api/payment-methods");
        Assert.AreEqual(0, methods!.PaymentMethods.Count);

        var thirdOrder = await CreateOrderAsync(client, 4);
        var deletedCardPayment = await client.PostAsJsonAsync($"api/orders/{thirdOrder.OrderId}/pay",
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, deletedCardPayment.StatusCode);
    }

    private static async Task<CreateOrderResponse> CreateOrderAsync(HttpClient client, int itemId)
    {
        return await PostAsync<CreateOrderResponse>(client, "api/orders", new CreateOrderRequest
        {
            Items = new List<CreateOrderLineRequest>
            {
                new() { CatalogItemId = itemId, Quantity = 1 }
            },
            ShippingAddress = new ShippingAddressRequest
            {
                Street = "1 Test Street", City = "Test City", State = "CA",
                Country = "US", ZipCode = "95131"
            }
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(IPayPalGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["PayPal:Currency"] = "TST"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton(gateway);
            });
        });

    private static CardRequest TestCard() => new()
    {
        Name = "Test Buyer", Number = "4111111111111111", Expiry = "2030-12",
        SecurityCode = "123",
        BillingAddress = new CardBillingAddressRequest
        {
            AddressLine1 = "1 Test Street", City = "Test City", State = "CA",
            PostalCode = "95131", CountryCode = "US"
        }
    };

    private static async Task<T> PostAsync<T>(HttpClient client, string uri, object body)
    {
        var response = await client.PostAsJsonAsync(uri, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode,
            $"POST {uri} failed with {(int)response.StatusCode}: {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        private readonly Dictionary<string, (decimal Amount, string Currency)> _orders = new();
        private readonly Dictionary<string, PayPalCaptureResult> _captures = new();
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int ReauthorizeCalls { get; private set; }
        public int VoidCalls { get; private set; }
        public TimeSpan AuthorizationAge { get; set; }
        public string? LastVaultIdUsed { get; private set; }

        public Task<PayPalOrderResult> CreateOrderAsync(string reference, decimal amount,
            string currency, CancellationToken cancellationToken)
        {
            _orders[reference] = (amount, currency);
            return Task.FromResult(new PayPalOrderResult("ORDER-" + reference, "CREATED", null, null));
        }

        public Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId,
            CancellationToken cancellationToken)
        {
            var capture = _captures.Values.FirstOrDefault();
            return Task.FromResult(new PayPalOrderResult(payPalOrderId,
                capture == null ? "COMPLETED" : "COMPLETED", null, capture));
        }

        public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string reference,
            string payPalOrderId, CardPaymentSource? card, string? vaultId,
            CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastVaultIdUsed = vaultId;
            var order = _orders[reference];
            var createdAt = DateTimeOffset.UtcNow - AuthorizationAge;
            return Task.FromResult(new PayPalAuthorizationResult("AUTH-" + reference, "CREATED",
                order.Amount, order.Currency, createdAt,
                createdAt.AddDays(29), "COMPLETED"));
        }

        public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken)
        {
            var order = _orders.Single(x => authorizationId.EndsWith(x.Key)).Value;
            var createdAt = DateTimeOffset.UtcNow - AuthorizationAge;
            return Task.FromResult(new PayPalAuthorizationResult(authorizationId, "CREATED",
                order.Amount, order.Currency, createdAt,
                createdAt.AddDays(29), string.Empty));
        }

        public Task<PayPalAuthorizationResult> ReauthorizeAsync(string reference,
            string authorizationId, decimal amount, string currency,
            CancellationToken cancellationToken)
        {
            ReauthorizeCalls++;
            return Task.FromResult(new PayPalAuthorizationResult(authorizationId, "CREATED", amount,
                currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), string.Empty));
        }

        public Task<PayPalCaptureResult> CaptureAsync(string reference, string authorizationId,
            decimal amount, string currency, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            var result = new PayPalCaptureResult("CAPTURE-" + reference, "COMPLETED", amount,
                currency, 1m, amount - 1m, DateTimeOffset.UtcNow);
            _captures[result.Id] = result;
            return Task.FromResult(result);
        }

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
            CancellationToken cancellationToken) => Task.FromResult(_captures[captureId]);

        public Task VoidAsync(string reference, string authorizationId,
            CancellationToken cancellationToken)
        {
            VoidCalls++;
            return Task.CompletedTask;
        }

        public Task<PayPalRefundResult> RefundAsync(string reference, string captureId,
            string idempotencyKey, decimal amount, string currency, string? note,
            CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefundResult("REFUND-" + RefundCalls, "COMPLETED",
                amount, currency, 0m, amount, DateTimeOffset.UtcNow));
        }

        public Task<SavedCardResult> SaveCardAsync(string merchantCustomerId,
            CardPaymentSource card, CancellationToken cancellationToken) =>
            Task.FromResult(new SavedCardResult("VAULT-1", "VISA", "1111", card.Expiry));

        public Task DeletePaymentTokenAsync(string vaultId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransactionResult>>(
                Array.Empty<PayPalTransactionResult>());
    }
}
