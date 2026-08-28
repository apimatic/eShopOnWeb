using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public async Task EnforcesOwnershipRolesAndIdempotentPaymentLifecycle()
    {
        var fakePayPal = new FakePayPalClient();
        await using var factory = new PaymentApiFactory(fakePayPal);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<PlaceOrderResponse>();
        Assert.IsNotNull(order);
        Assert.IsTrue(order.OrderId > 0);

        var payRequest = new PayOrderRequest { Card = TestCard() };
        var pay1 = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", payRequest);
        var pay2 = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", payRequest);
        Assert.AreEqual(HttpStatusCode.OK, pay1.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, pay2.StatusCode);
        Assert.AreEqual(1, fakePayPal.AuthorizationCalls, "A repeated pay call must not authorize twice.");

        var forbiddenFulfil = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);
        var fulfil1 = await admin.PostAsJsonAsync($"/api/orders/{order.OrderId}/fulfil", new { });
        var fulfil2 = await admin.PostAsJsonAsync($"/api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.OK, fulfil1.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, fulfil2.StatusCode);
        Assert.AreEqual(1, fakePayPal.CaptureCalls, "A repeated fulfil call must not capture twice.");

        var refund1 = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds",
            new RefundOrderRequest { IdempotencyKey = "return-one", Amount = 5m });
        var refundReplay = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds",
            new RefundOrderRequest { IdempotencyKey = "return-one", Amount = 5m });
        var refund2 = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds",
            new RefundOrderRequest { IdempotencyKey = "return-two", Amount = 5m });
        Assert.AreEqual(HttpStatusCode.Created, refund1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, refundReplay.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, refund2.StatusCode);
        Assert.AreEqual(2, fakePayPal.RefundCalls, "Same keys replay; distinct partial-refund keys remain valid.");
        var firstRefund = await refund1.Content.ReadFromJsonAsync<RefundOrderResponse>();
        var replayedRefund = await refundReplay.Content.ReadFromJsonAsync<RefundOrderResponse>();
        Assert.AreEqual(firstRefund!.RefundId, replayedRefund!.RefundId);

        var overRefund = await shopper.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds",
            new RefundOrderRequest { IdempotencyKey = "too-large", Amount = order.Total });
        Assert.AreEqual(HttpStatusCode.Conflict, overRefund.StatusCode);

        var savedResponse = await shopper.PostAsJsonAsync("/api/payment-methods",
            new SavePaymentMethodRequest { Alias = "Visa", Card = TestCard() });
        Assert.AreEqual(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<SavePaymentMethodResponse>();
        Assert.IsNotNull(saved);
        Assert.IsTrue(saved.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.Last4);

        var adminMethods = await admin.GetFromJsonAsync<List<PaymentMethodDto>>("/api/payment-methods");
        Assert.AreEqual(0, adminMethods!.Count, "Another shopper must not see saved cards.");
        var adminDelete = await admin.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, adminDelete.StatusCode);

        var secondOrderResponse = await shopper.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        });
        var secondOrder = await secondOrderResponse.Content.ReadFromJsonAsync<PlaceOrderResponse>();
        var savedPay = await shopper.PostAsJsonAsync($"/api/orders/{secondOrder!.OrderId}/pay",
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.OK, savedPay.StatusCode);
        var cancelled = await admin.PostAsJsonAsync($"/api/orders/{secondOrder.OrderId}/cancel", new { });
        Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.AreEqual(1, fakePayPal.VoidCalls);

        var deleted = await shopper.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await shopper.GetFromJsonAsync<List<PaymentMethodDto>>("/api/payment-methods");
        Assert.AreEqual(0, remaining!.Count);

        var report = await admin.GetAsync($"/api/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.OK, report.StatusCode);
    }

    private static CardDetails TestCard() => new()
    {
        Number = "4111111111111111",
        Expiry = "2030-12",
        SecurityCode = "115",
        Name = "Test Shopper",
        BillingAddress = new BillingAddress
        {
            AddressLine1 = "1 Main St",
            AdminArea1 = "CA",
            AdminArea2 = "San Jose",
            PostalCode = "95131",
            CountryCode = "US"
        }
    };

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakePayPalClient _fake;
        public PaymentApiFactory(FakePayPalClient fake) => _fake = fake;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["PayPal:ClientId"] = "test-client",
                    ["PayPal:ClientSecret"] = "test-secret",
                    ["PayPal:Environment"] = "Sandbox",
                    ["PayPal:Currency"] = "USD"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(_fake);
            });
        }
    }

    private sealed class FakePayPalClient : IPayPalClient
    {
        public int AuthorizationCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int VoidCalls { get; private set; }

        public Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, string? paypalCustomerId,
            CardDetails card, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new VaultedCardResult("VAULT-1", paypalCustomerId ?? "CUSTOMER-1", "VISA", "1111", "2030-12"));

        public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalAuthorizationResult> AuthorizeCardAsync(string orderReference, decimal amount,
            string currency, CardDetails card, string requestId, CancellationToken cancellationToken) =>
            Authorize(amount, currency);

        public Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(string orderReference, decimal amount,
            string currency, string vaultId, string requestId, CancellationToken cancellationToken) =>
            Authorize(amount, currency);

        private Task<PayPalAuthorizationResult> Authorize(decimal amount, string currency)
        {
            AuthorizationCalls++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PayPalAuthorizationResult($"ORDER-{AuthorizationCalls}",
                $"AUTH-{AuthorizationCalls}", "CREATED", amount, currency, now, now.AddDays(29)));
        }

        public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PayPalAuthorizationResult(string.Empty, authorizationId, "CREATED",
                authorizationId == "AUTH-1" ? 19.5m : 8.5m, "USD", now, now.AddDays(29)));
        }

        public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCaptureResult("CAPTURE-1", "COMPLETED", amount, currency,
                1m, amount - 1m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCaptureResult(captureId, "COMPLETED", 19.5m, "USD", 1m, 18.5m,
                DateTimeOffset.UtcNow));

        public Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
        {
            VoidCalls++;
            return Task.FromResult("VOIDED");
        }

        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefundResult($"REFUND-{RefundCalls}", "COMPLETED", amount,
                currency, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyCollection<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
