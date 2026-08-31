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

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowEndpointTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(new FakePayPalGateway());
            }));
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup() { _client.Dispose(); _factory.Dispose(); }

    [TestMethod]
    public async Task CompleteFlowIsStatefulIdempotentAndRoleProtected()
    {
        var user = ApiTokenHelper.GetNormalUserToken();
        var admin = ApiTokenHelper.GetAdminUserToken();
        UseToken(user);
        var created = await _client.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<CreatedOrder>();
        Assert.IsNotNull(order);

        UseToken(admin);
        var forbiddenOwnership = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", new { paymentMethodId = 42 });
        Assert.AreEqual(HttpStatusCode.NotFound, forbiddenOwnership.StatusCode);

        UseToken(user);
        var pay = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", new { card = TestCard() });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
        StringAssert.Contains(await pay.Content.ReadAsStringAsync(), "Authorized");

        var forbiddenFulfil = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);

        UseToken(admin);
        var fulfil = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);
        var fulfilledBody = await fulfil.Content.ReadAsStringAsync();
        StringAssert.Contains(fulfilledBody, "Fulfilled");
        StringAssert.Contains(fulfilledBody, "paypalFee");

        UseToken(user);
        var refundRequest = new { amount = 5.25m, idempotencyKey = "same-key" };
        var first = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds", refundRequest);
        var second = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds", refundRequest);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        var excessive = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/refunds", new { amount = 20m, idempotencyKey = "different-key" });
        Assert.AreEqual(HttpStatusCode.Conflict, excessive.StatusCode);
    }

    [TestMethod]
    public async Task SavedCardCanBeListedDeletedAndNoLongerUsed()
    {
        UseToken(ApiTokenHelper.GetNormalUserToken());
        var savedResponse = await _client.PostAsJsonAsync("/api/payment-methods", new { card = TestCard() });
        Assert.AreEqual(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<SavedMethod>(); Assert.IsNotNull(saved);
        var list = await _client.GetStringAsync("/api/payment-methods");
        StringAssert.Contains(list, "1111"); Assert.IsFalse(list.Contains("4111111111111111", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}")).StatusCode);
        var order = await (await _client.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } })).Content.ReadFromJsonAsync<CreatedOrder>();
        var pay = await _client.PostAsJsonAsync($"/api/orders/{order!.OrderId}/pay", new { paymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
    }

    private void UseToken(string token) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static object TestCard() => new { number = "4111111111111111", expiry = "2028-12", securityCode = "123", name = "Test Shopper", billingAddress = new { addressLine1 = "1 Main St", adminArea2 = "San Jose", adminArea1 = "CA", postalCode = "95131", countryCode = "US" } };
    private sealed record CreatedOrder(int OrderId);
    private sealed record SavedMethod(int PaymentMethodId);

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        public string Currency => "USD";
        public Task<PayPalAuthorization> AuthorizeAsync(string reference, decimal amount, CardDto? card, string? vaultId, CancellationToken ct) => Task.FromResult(new PayPalAuthorization("PP-ORDER-" + reference, "AUTH-" + reference, "CREATED", amount, Currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        public Task<PayPalAuthorization> ReauthorizeAsync(string reference, string authorizationId, CancellationToken ct) => Task.FromResult(new PayPalAuthorization(string.Empty, "REAUTH-" + reference, "CREATED", 19.5m, Currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(26)));
        public Task<PayPalCapture> CaptureAsync(string reference, string authorizationId, decimal amount, CancellationToken ct) => Task.FromResult(new PayPalCapture("CAP-" + reference, "COMPLETED", amount, Currency, 1m, amount - 1m, DateTimeOffset.UtcNow));
        public Task<string> VoidAsync(string reference, string authorizationId, CancellationToken ct) => Task.FromResult("VOIDED");
        public Task<PayPalRefund> RefundAsync(string reference, string captureId, decimal amount, string key, CancellationToken ct) => Task.FromResult(new PayPalRefund("REF-" + key, "COMPLETED", amount, Currency, DateTimeOffset.UtcNow));
        public Task<PayPalSavedCard> SaveCardAsync(string shopperId, string? customerId, CardDto card, CancellationToken ct) => Task.FromResult(new PayPalSavedCard("TOKEN-1", "CUSTOMER-1", "VISA", "1111", "2028-12", card.Name));
        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
