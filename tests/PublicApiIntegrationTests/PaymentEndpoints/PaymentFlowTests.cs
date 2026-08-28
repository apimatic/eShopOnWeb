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
public class PaymentFlowTests
{
    [TestMethod]
    public async Task FullPaymentSavedCardAndReconciliationFlowWorks()
    {
        var payPal = new FakePayPalClient();
        await using var factory = new PaymentApiFactory(payPal);
        using var client = factory.CreateClient();
        SetToken(client, ApiTokenHelper.GetNormalUserToken());

        var orderId = await CreateOrderAsync(client);
        using (var pay = await client.PostAsJsonAsync($"/api/orders/{orderId}/pay", new
        {
            card = TestCard()
        }))
        {
            Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
            var json = await JsonDocument.ParseAsync(await pay.Content.ReadAsStreamAsync());
            Assert.AreEqual("Authorized", json.RootElement.GetProperty("payment").GetProperty("status").GetString());
        }
        using (var repeatedPay = await client.PostAsJsonAsync($"/api/orders/{orderId}/pay", new
        {
            card = TestCard()
        }))
        {
            Assert.AreEqual(HttpStatusCode.OK, repeatedPay.StatusCode);
            Assert.AreEqual(1, payPal.AuthorizeCalls);
        }

        SetToken(client, ApiTokenHelper.GetUserToken("other-shopper@example.com"));
        using (var crossShopperPay = await client.PostAsJsonAsync($"/api/orders/{orderId}/pay", new { card = TestCard() }))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, crossShopperPay.StatusCode);
        }

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        using (var forbiddenFulfil = await client.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { }))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);
        }

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        using (var fulfil = await client.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { }))
        {
            Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);
            var json = await JsonDocument.ParseAsync(await fulfil.Content.ReadAsStreamAsync());
            Assert.AreEqual("Fulfilled", json.RootElement.GetProperty("fulfillmentStatus").GetString());
            Assert.AreEqual(0.59m, json.RootElement.GetProperty("payment").GetProperty("capture").GetProperty("payPalFee").GetDecimal());
        }
        using (var repeatedFulfil = await client.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { }))
        {
            Assert.AreEqual(HttpStatusCode.OK, repeatedFulfil.StatusCode);
            Assert.AreEqual(1, payPal.CaptureCalls);
        }

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        string firstRefundId;
        using (var refund = await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new
        {
            amount = 5.00m,
            idempotencyKey = "return-line-1"
        }))
        {
            Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
            var json = await JsonDocument.ParseAsync(await refund.Content.ReadAsStreamAsync());
            firstRefundId = json.RootElement.GetProperty("refundId").GetString()!;
        }
        using (var repeatedRefund = await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new
        {
            amount = 6.00m,
            idempotencyKey = "return-line-1"
        }))
        {
            Assert.AreEqual(HttpStatusCode.Created, repeatedRefund.StatusCode);
            var json = await JsonDocument.ParseAsync(await repeatedRefund.Content.ReadAsStreamAsync());
            Assert.AreEqual(firstRefundId, json.RootElement.GetProperty("refundId").GetString());
            Assert.AreEqual(1, payPal.RefundCalls);
        }
        using (var excessiveRefund = await client.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new
        {
            amount = 999.00m,
            idempotencyKey = "too-much"
        }))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, excessiveRefund.StatusCode);
            Assert.AreEqual(1, payPal.RefundCalls);
        }

        int paymentMethodId;
        using (var saveMethod = await client.PostAsJsonAsync("/api/payment-methods", new { card = TestCard() }))
        {
            Assert.AreEqual(HttpStatusCode.Created, saveMethod.StatusCode);
            var json = await JsonDocument.ParseAsync(await saveMethod.Content.ReadAsStreamAsync());
            paymentMethodId = json.RootElement.GetProperty("paymentMethodId").GetInt32();
            Assert.AreEqual("1111", json.RootElement.GetProperty("paymentMethod").GetProperty("lastDigits").GetString());
        }

        SetToken(client, ApiTokenHelper.GetUserToken("other-shopper@example.com"));
        using (var crossShopperList = await client.GetAsync("/api/payment-methods"))
        {
            var json = await JsonDocument.ParseAsync(await crossShopperList.Content.ReadAsStreamAsync());
            Assert.AreEqual(0, json.RootElement.GetProperty("paymentMethods").GetArrayLength());
        }
        using (var crossShopperDelete = await client.DeleteAsync($"/api/payment-methods/{paymentMethodId}"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, crossShopperDelete.StatusCode);
        }
        var crossShopperOrderId = await CreateOrderAsync(client);
        using (var crossShopperUse = await client.PostAsJsonAsync($"/api/orders/{crossShopperOrderId}/pay", new { paymentMethodId }))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, crossShopperUse.StatusCode);
        }

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        var secondOrderId = await CreateOrderAsync(client);
        using (var paySaved = await client.PostAsJsonAsync($"/api/orders/{secondOrderId}/pay", new { paymentMethodId }))
        {
            Assert.AreEqual(HttpStatusCode.OK, paySaved.StatusCode);
        }

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        using (var cancel = await client.PostAsJsonAsync($"/api/orders/{secondOrderId}/cancel", new { }))
        {
            Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
            var json = await JsonDocument.ParseAsync(await cancel.Content.ReadAsStreamAsync());
            Assert.AreEqual("Cancelled", json.RootElement.GetProperty("fulfillmentStatus").GetString());
            Assert.AreEqual("Voided", json.RootElement.GetProperty("payment").GetProperty("status").GetString());
        }
        using (var repeatedCancel = await client.PostAsJsonAsync($"/api/orders/{secondOrderId}/cancel", new { }))
        {
            Assert.AreEqual(HttpStatusCode.OK, repeatedCancel.StatusCode);
            Assert.AreEqual(1, payPal.VoidCalls);
        }

        SetToken(client, ApiTokenHelper.GetNormalUserToken());
        using (var delete = await client.DeleteAsync($"/api/payment-methods/{paymentMethodId}"))
        {
            Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        }
        using (var list = await client.GetAsync("/api/payment-methods"))
        {
            var json = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
            Assert.AreEqual(0, json.RootElement.GetProperty("paymentMethods").GetArrayLength());
        }
        var thirdOrderId = await CreateOrderAsync(client);
        using (var removedMethodPay = await client.PostAsJsonAsync($"/api/orders/{thirdOrderId}/pay", new { paymentMethodId }))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, removedMethodPay.StatusCode);
        }

        SetToken(client, ApiTokenHelper.GetAdminUserToken());
        var from = DateTimeOffset.UtcNow.AddDays(-40).ToString("O");
        var to = DateTimeOffset.UtcNow.ToString("O");
        using (var reconciliation = await client.GetAsync($"/api/reconciliation?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
            var json = await JsonDocument.ParseAsync(await reconciliation.Content.ReadAsStreamAsync());
            Assert.AreEqual(4, json.RootElement.GetProperty("payPalTransactions").GetArrayLength());
            Assert.AreEqual(4, payPal.SearchCalls);
        }
    }

    private static async Task<int> CreateOrderAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new
            {
                street = "2211 N First Street",
                city = "San Jose",
                state = "CA",
                country = "US",
                zipCode = "95131"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("orderId").GetInt32();
    }

    private static object TestCard() => new
    {
        number = "4" + new string('1', 15),
        expiry = "2030-12",
        securityCode = "123",
        name = "Test Shopper",
        billingAddress = new
        {
            addressLine1 = "2211 N First Street",
            addressLine2 = (string?)null,
            adminArea2 = "San Jose",
            adminArea1 = "CA",
            postalCode = "95131",
            countryCode = "US"
        }
    };

    private static void SetToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakePayPalClient _payPal;
        public PaymentApiFactory(FakePayPalClient payPal) => _payPal = payPal;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["PayPal:ClientId"] = "test-client",
                    ["PayPal:ClientSecret"] = "test-secret",
                    ["PayPal:Environment"] = "sandbox",
                    ["PayPal:Currency"] = "USD"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(_payPal);
            });
        }
    }

    private sealed class FakePayPalClient : IPayPalClient
    {
        private int _sequence;
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int VoidCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int SearchCalls { get; private set; }

        public Task<string> CreateOrderAsync(int orderId, decimal amount, string currency, string invoiceId, CancellationToken cancellationToken)
        {
            return Task.FromResult($"ORDER-{orderId}");
        }

        public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, int orderId, decimal amount, string currency, CardInput? card, string? vaultId, int authorizationAttempt, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PayPalAuthorizationResult(
                payPalOrderId, $"AUTH-{orderId}", "CREATED", amount, currency,
                now, now.AddDays(29), "VISA", "1111"));
        }

        public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string payPalOrderId, decimal amount, string currency, DateTimeOffset originalExpirationTime, string requestId, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PayPalAuthorizationResult(
                payPalOrderId, $"REAUTH-{authorizationId}", "CREATED", amount, currency,
                now, originalExpirationTime, null, null));
        }

        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string invoiceId, decimal amount, string currency, string requestId, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCaptureResult(
                $"CAPTURE-{invoiceId}", "COMPLETED", amount, currency, 0.59m, amount - 0.59m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string> VoidAsync(string authorizationId, CancellationToken cancellationToken)
        {
            VoidCalls++;
            return Task.FromResult("VOIDED");
        }

        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefundResult(
                $"REFUND-{RefundCalls}", "COMPLETED", amount, currency, DateTimeOffset.UtcNow));
        }

        public Task<PayPalSavedCardResult> SaveCardAsync(CardInput card, string merchantCustomerId, string? payPalCustomerId, string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PayPalSavedCardResult(
                $"TOKEN-{Interlocked.Increment(ref _sequence)}", payPalCustomerId ?? "CUSTOMER-1", "VISA", "1111", card.Expiry));
        }

        public Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken)
        {
            SearchCalls++;
            var id = $"TX-{SearchCalls}";
            IReadOnlyList<PayPalTransaction> transactions = new[]
            {
                new PayPalTransaction(id, null, null, "T0006", "S", from, from, 19.50m, "USD", 0.59m, null)
            };
            return Task.FromResult(new PayPalTransactionPage(transactions, page, 2));
        }
    }
}
