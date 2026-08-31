using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
public class PaymentEndpointsTest
{
    [TestMethod]
    public async Task DrivesPaymentLifecycleWithOwnershipRolesAndIdempotency()
    {
        var provider = new FakePayPalGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("UseOnlyInMemoryDatabase", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(provider);
                services.RemoveAll<PayPalOptions>();
                services.AddSingleton(new PayPalOptions
                {
                    ClientId = "test-only",
                    ClientSecret = "test-only",
                    Environment = "Sandbox",
                    Currency = "USD"
                });
            });
        });
        using var client = factory.CreateClient();

        SetShopper(client);
        var firstOrder = await Post(client, "/api/orders", OrderBody(1));
        Assert.AreEqual(HttpStatusCode.Created, firstOrder.StatusCode);
        var firstOrderId = await ReadInt(firstOrder, "orderId");

        var firstPay = await Post(client, $"/api/orders/{firstOrderId}/pay", CardPaymentBody());
        Assert.AreEqual(HttpStatusCode.OK, firstPay.StatusCode);
        var replayPay = await Post(client, $"/api/orders/{firstOrderId}/pay", CardPaymentBody());
        Assert.AreEqual(HttpStatusCode.OK, replayPay.StatusCode);
        Assert.AreEqual(1, provider.AuthorizeCalls, "A repeated pay request must not authorize twice.");

        var forbiddenFulfil = await Post(client, $"/api/orders/{firstOrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenFulfil.StatusCode);

        SetAdmin(client);
        var fulfil = await Post(client, $"/api/orders/{firstOrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);
        Assert.AreEqual("Captured", await ReadNestedString(fulfil, "payment", "status"));

        SetShopper(client);
        var refund = await Post(client, $"/api/orders/{firstOrderId}/refunds", new
        {
            amount = 5.00m,
            idempotencyKey = "same-refund"
        });
        var refundReplay = await Post(client, $"/api/orders/{firstOrderId}/refunds", new
        {
            amount = 5.00m,
            idempotencyKey = "same-refund"
        });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
        Assert.AreEqual(await ReadInt(refund, "refundId"), await ReadInt(refundReplay, "refundId"));
        Assert.AreEqual(1, provider.RefundCalls, "A repeated refund key must not call PayPal twice.");

        var saved = await Post(client, "/api/payment-methods", new { card = CardBody() });
        var paymentMethodId = await ReadInt(saved, "paymentMethodId");
        Assert.AreEqual(HttpStatusCode.Created, saved.StatusCode);

        SetAdmin(client);
        var adminList = await client.GetAsync("/api/payment-methods");
        Assert.AreEqual("[]", await adminList.Content.ReadAsStringAsync());
        var crossUserDelete = await client.DeleteAsync($"/api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossUserDelete.StatusCode);

        SetShopper(client);
        var secondOrder = await Post(client, "/api/orders", OrderBody(2));
        var secondOrderId = await ReadInt(secondOrder, "orderId");
        var savedCardPay = await Post(client, $"/api/orders/{secondOrderId}/pay", new
        {
            paymentMethodId,
            card = (object?)null
        });
        Assert.AreEqual(HttpStatusCode.OK, savedCardPay.StatusCode);
        Assert.AreEqual("vault-token", provider.LastVaultId);

        SetAdmin(client);
        var cancel = await Post(client, $"/api/orders/{secondOrderId}/cancel", null);
        Assert.AreEqual("Cancelled", await ReadString(cancel, "status"));
        Assert.AreEqual("Voided", await ReadNestedString(cancel, "payment", "status"));

        SetShopper(client);
        var delete = await client.DeleteAsync($"/api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var methods = await client.GetAsync("/api/payment-methods");
        Assert.AreEqual("[]", await methods.Content.ReadAsStringAsync());
    }

    private static object OrderBody(int catalogItemId) => new
    {
        items = new[] { new { catalogItemId, quantity = 1 } },
        shippingAddress = new { street = "1 Test St", city = "Test", state = "WA", country = "US", zipCode = "98000" }
    };

    private static object CardPaymentBody() => new { card = CardBody(), paymentMethodId = (int?)null };

    private static object CardBody() => new
    {
        name = "Test Shopper",
        number = "transport-test-pan",
        expiry = "2030-12",
        securityCode = "test-cvc",
        billingAddress = new
        {
            addressLine1 = "1 Test St",
            city = "Test",
            state = "WA",
            postalCode = "98000",
            countryCode = "US"
        }
    };

    private static async Task<HttpResponseMessage> Post(HttpClient client, string uri, object? body)
    {
        HttpContent? content = body is null
            ? null
            : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await client.PostAsync(uri, content);
    }

    private static async Task<int> ReadInt(HttpResponseMessage response, string property)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(property).GetInt32();
    }

    private static async Task<string> ReadString(HttpResponseMessage response, string property)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(property).GetString()!;
    }

    private static async Task<string> ReadNestedString(HttpResponseMessage response, string parent, string property)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(parent).GetProperty(property).GetString()!;
    }

    private static void SetShopper(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

    private static void SetAdmin(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        public int AuthorizeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastVaultId { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(int localOrderId, decimal amount, CardInput? card, string? vaultId, string createRequestId, string authorizeRequestId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastVaultId = vaultId;
            return Task.FromResult(new AuthorizationResult(
                $"provider-order-{localOrderId}", "COMPLETED", $"authorization-{localOrderId}", "CREATED",
                amount, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        }

        public Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
            Task.FromResult<(string, string, DateTimeOffset?, DateTimeOffset?)>((authorizationId, "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));

        public Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult<(string, string, DateTimeOffset?, DateTimeOffset?)>((authorizationId, "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));

        public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new CaptureResult("capture-1", "COMPLETED", amount, "USD", 1m, amount - 1m, DateTimeOffset.UtcNow));

        public Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new CaptureResult(captureId, "COMPLETED", 19.5m, "USD", 1m, 18.5m, DateTimeOffset.UtcNow));

        public Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult("VOIDED");

        public Task<RefundProviderResult> RefundAsync(string captureId, decimal amount, bool fullRemainder, string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new RefundProviderResult($"refund-{RefundCalls}", "COMPLETED", amount, "USD", DateTimeOffset.UtcNow));
        }

        public Task<RefundProviderResult> GetRefundAsync(string refundId, decimal expectedAmount, CancellationToken cancellationToken) =>
            Task.FromResult(new RefundProviderResult(refundId, "COMPLETED", expectedAmount, "USD", DateTimeOffset.UtcNow));

        public Task<SavedCardProviderResult> SaveCardAsync(string buyerId, CardInput card, string setupRequestId, string tokenRequestId, CancellationToken cancellationToken) =>
            Task.FromResult(new SavedCardProviderResult("vault-token", "customer-1", "VISA", "CREDIT", "1111", "2030-12"));

        public Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult(new TransactionSearchResult(Array.Empty<ProviderTransaction>(), DateTimeOffset.UtcNow));
    }
}
