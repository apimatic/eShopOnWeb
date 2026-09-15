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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowEndpointTest
{
    [TestMethod]
    public async Task RunsAuthorizeCaptureRefundVaultReuseAndOwnershipFlow()
    {
        using var factory = new PaymentApiFactory();
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var order = await Post<PlaceOrderResponse>(shopper, "/api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        }, HttpStatusCode.Created);
        Assert.IsTrue(order.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", order.PaymentStatus);

        var crossOwnerPay = await admin.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", new
        {
            card = Card()
        });
        Assert.AreEqual(HttpStatusCode.NotFound, crossOwnerPay.StatusCode);

        var authorized = await Post<PaymentResponse>(shopper, $"/api/orders/{order.OrderId}/pay",
            new { card = Card() }, HttpStatusCode.OK);
        Assert.AreEqual("Authorized", authorized.PaymentStatus);

        var fulfilled = await Post<FulfilResponse>(admin, $"/api/orders/{order.OrderId}/fulfil",
            new { }, HttpStatusCode.OK);
        Assert.AreEqual("Captured", fulfilled.PaymentStatus);
        Assert.AreEqual(order.Total, fulfilled.CapturedAmount);
        Assert.IsNotNull(fulfilled.PayPalFee);
        Assert.IsNotNull(fulfilled.NetProceeds);

        const string refundKey = "partial-refund-1";
        var refund = await Post<RefundResponse>(shopper, $"/api/orders/{order.OrderId}/refunds",
            new { idempotencyKey = refundKey, amount = 3.25m }, HttpStatusCode.OK);
        var repeated = await Post<RefundResponse>(shopper, $"/api/orders/{order.OrderId}/refunds",
            new { idempotencyKey = refundKey, amount = 3.25m }, HttpStatusCode.OK);
        Assert.AreEqual(refund.RefundId, repeated.RefundId);
        Assert.AreEqual(1, factory.Gateway.RefundWrites);

        var saved = await Post<PaymentMethodResponse>(shopper, "/api/payment-methods",
            new { card = Card() }, HttpStatusCode.Created);
        Assert.IsTrue(saved.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.LastDigits);

        var second = await Post<PlaceOrderResponse>(shopper, "/api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        }, HttpStatusCode.Created);
        var reused = await Post<PaymentResponse>(shopper, $"/api/orders/{second.OrderId}/pay",
            new { paymentMethodId = saved.PaymentMethodId }, HttpStatusCode.OK);
        Assert.AreEqual("Authorized", reused.PaymentStatus);
        Assert.AreEqual(1, factory.Gateway.VaultAuthorizationWrites);

        var deleted = await shopper.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        var listed = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("/api/payment-methods");
        Assert.IsNotNull(listed);
        Assert.AreEqual(0, listed.Count);
    }

    private static object Card() => new
    {
        name = "Sandbox Shopper",
        number = "4111111111111111",
        expiry = "2035-12",
        securityCode = "123",
        billingAddress = new { countryCode = "US", postalCode = "95131" }
    };

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static async Task<T> Post<T>(HttpClient client, string uri, object body, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync(uri, body);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}

internal sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPalGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseOnlyInMemoryDatabase"] = "true",
            ["PayPal:ClientId"] = Guid.NewGuid().ToString("N"),
            ["PayPal:ClientSecret"] = Guid.NewGuid().ToString("N"),
            ["PayPal:Environment"] = "sandbox",
            ["PayPal:Currency"] = "ZZZ"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPayPalGateway>();
            services.AddSingleton<IPayPalGateway>(Gateway);
        });
    }
}

internal sealed class FakePayPalGateway : IPayPalGateway
{
    private readonly Dictionary<string, ProviderSavedCard> _cards = new();
    private int _sequence;
    public int RefundWrites { get; private set; }
    public int VaultAuthorizationWrites { get; private set; }

    public Task<ProviderAuthorization> AuthorizeAsync(int orderId, string operationId, decimal amount,
        string currency, CardInput? card, string? vaultId, CancellationToken cancellationToken)
    {
        if (vaultId is not null) VaultAuthorizationWrites++;
        return Task.FromResult(new ProviderAuthorization($"ORDER-{orderId}", "COMPLETED", $"AUTH-{orderId}",
            "CREATED", amount, currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, string paypalOrderId,
        CancellationToken cancellationToken) => Task.FromResult(new ProviderAuthorization(paypalOrderId,
        "COMPLETED", authorizationId, "CREATED", AmountFor(authorizationId), "ZZZ", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddDays(3)));

    public Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string paypalOrderId,
        string requestId, decimal amount, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderAuthorization(paypalOrderId, "COMPLETED", authorizationId + "-R", "CREATED",
            amount, currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));

    public Task<ProviderCapture> CaptureAsync(string authorizationId, string requestId, int orderId,
        decimal amount, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderCapture($"CAP-{orderId}", "COMPLETED", amount, currency, 1m,
            amount - 1m, DateTimeOffset.UtcNow));

    public Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
        Task.FromResult("VOIDED");

    public Task<ProviderRefund> RefundAsync(string captureId, string idempotencyKey, decimal? amount,
        string currency, CancellationToken cancellationToken)
    {
        RefundWrites++;
        return Task.FromResult(new ProviderRefund($"REF-{++_sequence}", "COMPLETED", amount ?? 0m,
            currency, DateTimeOffset.UtcNow));
    }

    public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ProviderSavedCard> SaveCardAsync(string ownerId, string requestId, CardInput card,
        string? existingCustomerId, CancellationToken cancellationToken)
    {
        var saved = new ProviderSavedCard($"TOKEN-{++_sequence}", existingCustomerId ?? $"CUSTOMER-{ownerId}",
            "VISA", card.Number[^4..], card.Expiry, "CREDIT");
        _cards.Add(saved.TokenId, saved);
        return Task.FromResult(saved);
    }

    public Task<IReadOnlyList<ProviderSavedCard>> ListCardsAsync(string customerId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderSavedCard>>(
        _cards.Values.Where(x => x.CustomerId == customerId).ToList());

    public Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken)
    {
        _cards.Remove(tokenId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProviderTransaction>>(Array.Empty<ProviderTransaction>());

    private static decimal AmountFor(string authorizationId) => authorizationId.EndsWith("1") ? 39m : 8.5m;
}
