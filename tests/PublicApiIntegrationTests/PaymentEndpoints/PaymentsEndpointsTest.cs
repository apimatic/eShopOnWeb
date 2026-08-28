using System;
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
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentsEndpointsTest
{
    [TestMethod]
    public async Task CompletePaymentAndVaultFlowsAreScopedAndIdempotent()
    {
        var fake = new FakePayPalGateway();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(fake);
            }));
        using var client = factory.CreateClient();

        SetShopper(client);
        var orderId = await CreateOrder(client);
        using var payResponse = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardPayment());
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        using var payAgain = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardPayment());
        Assert.AreEqual(HttpStatusCode.OK, payAgain.StatusCode);
        Assert.AreEqual(1, fake.AuthorizeCalls, "A repeated pay request must not authorize twice.");

        using var shopperFulfil = await client.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperFulfil.StatusCode);
        SetAdmin(client);
        using var fulfil = await client.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);
        using var fulfilAgain = await client.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.OK, fulfilAgain.StatusCode);
        Assert.AreEqual(1, fake.CaptureCalls, "A repeated fulfil request must not capture twice.");

        SetShopper(client);
        using var refund = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 5.00m, idempotencyKey = "return-one" });
        Assert.AreEqual(HttpStatusCode.OK, refund.StatusCode);
        var refundJson = await refund.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(refundJson.GetProperty("refundId").GetString()));
        using var refundAgain = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 5.00m, idempotencyKey = "return-one" });
        Assert.AreEqual(HttpStatusCode.OK, refundAgain.StatusCode);
        Assert.AreEqual(1, fake.RefundCalls, "A repeated refund key must not refund twice.");
        using var finalRefund = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { idempotencyKey = "return-remainder" });
        Assert.AreEqual(HttpStatusCode.OK, finalRefund.StatusCode);
        using var finalRefundAgain = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { idempotencyKey = "return-remainder" });
        Assert.AreEqual(HttpStatusCode.OK, finalRefundAgain.StatusCode,
            "A repeated full-refund key must remain idempotent after the order becomes Refunded.");
        Assert.AreEqual(2, fake.RefundCalls);

        using var savedResponse = await client.PostAsJsonAsync("api/payment-methods", Card());
        Assert.AreEqual(HttpStatusCode.Created, savedResponse.StatusCode);
        var savedJson = await savedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentMethodId = savedJson.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", savedJson.GetProperty("last4").GetString());
        Assert.IsFalse((await savedResponse.Content.ReadAsStringAsync()).Contains("4111111111111111"));

        SetAdmin(client);
        using var otherDelete = await client.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode,
            "Another shopper must not be able to delete a saved card.");

        SetShopper(client);
        var secondOrderId = await CreateOrder(client);
        using var savedPay = await client.PostAsJsonAsync($"api/orders/{secondOrderId}/pay",
            new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.OK, savedPay.StatusCode);
        Assert.AreEqual(2, fake.AuthorizeCalls);

        SetAdmin(client);
        using var cancel = await client.PostAsJsonAsync($"api/orders/{secondOrderId}/cancel", new { });
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.AreEqual(1, fake.VoidCalls);

        SetShopper(client);
        using var delete = await client.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var thirdOrderId = await CreateOrder(client);
        using var deletedPay = await client.PostAsJsonAsync($"api/orders/{thirdOrderId}/pay",
            new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, deletedPay.StatusCode,
            "A deleted saved card must no longer be usable.");

        SetAdmin(client);
        using var reconciliation = await client.GetAsync(
            $"api/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-40).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.AreEqual(HttpStatusCode.OK, reconciliation.StatusCode);
        Assert.AreEqual(1, fake.ReportingCalls);
    }

    private static async Task<int> CreateOrder(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shippingAddress = new
            {
                street = "2211 N First Street", city = "San Jose", state = "CA",
                country = "US", zipCode = "95131"
            }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("orderId").GetInt32();
    }

    private static object CardPayment() => new { card = Card() };
    private static object Card() => new
    {
        number = "4111111111111111", expiry = "2030-12", securityCode = "123", name = "Test Shopper",
        billingAddress = new
        {
            addressLine1 = "2211 N First Street", city = "San Jose", state = "CA",
            postalCode = "95131", countryCode = "US"
        }
    };

    private static void SetShopper(HttpClient client) => client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
    private static void SetAdmin(HttpClient client) => client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
}

internal sealed class FakePayPalGateway : IPayPalGateway
{
    public int AuthorizeCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int RefundCalls { get; private set; }
    public int VoidCalls { get; private set; }
    public int ReportingCalls { get; private set; }

    public Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        PaymentCardData? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        AuthorizeCalls++;
        return Task.FromResult(Auth(orderId, amount, currency, $"AUTH-{orderId}"));
    }

    public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
        => Task.FromResult(Auth(1, 19.50m, "USD", authorizationId));

    public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
        => Task.FromResult(Auth(1, amount, currency, authorizationId + "-R"));

    public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        CaptureCalls++;
        return Task.FromResult(new PayPalCaptureResult($"CAP-{orderId}", "COMPLETED", amount,
            currency, 1m, amount - 1m, DateTimeOffset.UtcNow));
    }

    public Task VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        VoidCalls++;
        return Task.CompletedTask;
    }

    public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        RefundCalls++;
        return Task.FromResult(new PayPalRefundResult($"REF-{RefundCalls}", "COMPLETED", amount,
            currency, DateTimeOffset.UtcNow));
    }

    public Task<PayPalVaultResult> SaveCardAsync(PaymentCardData card, string merchantCustomerId,
        string? paypalCustomerId, string requestId, CancellationToken cancellationToken)
        => Task.FromResult(new PayPalVaultResult("VAULT-1", "CUSTOMER-1", "VISA", "1111", "2030-12"));

    public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        ReportingCalls++;
        IReadOnlyList<PayPalTransaction> rows = Array.Empty<PayPalTransaction>();
        return Task.FromResult(rows);
    }

    private static PayPalAuthorizationResult Auth(int orderId, decimal amount, string currency, string id)
        => new($"PP-ORDER-{orderId}", id, "CREATED", amount, currency,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
}
