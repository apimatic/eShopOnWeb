using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PayPalSandboxFlowTest
{
    [TestMethod]
    [TestCategory("Sandbox")]
    public async Task AuthorizeCaptureRefundVaultReuseCancelAndDelete()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PAYPAL_RUN_SANDBOX_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("Set PAYPAL_RUN_SANDBOX_TESTS=true to run live PayPal sandbox verification.");

        var cardNumber = RequiredEnvironment("PAYPAL_SANDBOX_CARD_NUMBER");
        var cardCvc = RequiredEnvironment("PAYPAL_SANDBOX_CARD_CVC");
        SetConfigurationEnvironment("PayPal__ClientId", RequiredEnvironment("PAYPAL_CLIENT_ID"));
        SetConfigurationEnvironment("PayPal__ClientSecret", RequiredEnvironment("PAYPAL_CLIENT_SECRET"));
        SetConfigurationEnvironment("PayPal__Environment", RequiredEnvironment("PAYPAL_ENVIRONMENT"));
        SetConfigurationEnvironment("PayPal__Currency", RequiredEnvironment("PAYPAL_CURRENCY"));

        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();
        var shopperToken = await Authenticate(client, "demouser@microsoft.com", "Pass@word1");
        var adminToken = await Authenticate(client, "admin@microsoft.com", "Pass@word1");

        var orderId = await PlaceOrder(client, shopperToken, 1);
        var directPay = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{orderId}/pay", new
        {
            card = Card(cardNumber, cardCvc)
        });
        await AssertSuccessUnlessChallenge(directPay);
        var paid = await Json(directPay);
        Assert.AreEqual("Authorized", paid.RootElement.GetProperty("paymentStatus").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(paid.RootElement.GetProperty("authorizationId").GetString()));

        var fulfil = await Send(client, adminToken, HttpMethod.Post, $"api/orders/{orderId}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();
        var fulfilled = await Json(fulfil);
        Assert.AreEqual("Fulfilled", fulfilled.RootElement.GetProperty("fulfilmentStatus").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(fulfilled.RootElement.GetProperty("captureId").GetString()));
        Assert.AreEqual(19.5m, fulfilled.RootElement.GetProperty("capturedAmount").GetDecimal());
        Assert.AreNotEqual(JsonValueKind.Null, fulfilled.RootElement.GetProperty("payPalFee").ValueKind);
        Assert.AreNotEqual(JsonValueKind.Null, fulfilled.RootElement.GetProperty("netProceeds").ValueKind);

        var refundKey = $"sandbox-refund-{Guid.NewGuid():N}";
        var refund = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{orderId}/refunds", new
        {
            idempotencyKey = refundKey,
            amount = 5.00m
        });
        await AssertSuccessUnlessChallenge(refund);
        var refunded = await Json(refund);
        var refundId = refunded.RootElement.GetProperty("refundId").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(refundId));

        var repeatedRefund = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{orderId}/refunds", new
        {
            idempotencyKey = refundKey,
            amount = 5.00m
        });
        await AssertSuccessUnlessChallenge(repeatedRefund);
        var repeated = await Json(repeatedRefund);
        Assert.AreEqual(refundId, repeated.RootElement.GetProperty("refundId").GetString());

        var secondRefund = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{orderId}/refunds", new
        {
            idempotencyKey = $"sandbox-refund-{Guid.NewGuid():N}",
            amount = 1.00m
        });
        await AssertSuccessUnlessChallenge(secondRefund);
        var secondRefundJson = await Json(secondRefund);
        Assert.AreNotEqual(refundId, secondRefundJson.RootElement.GetProperty("refundId").GetString());

        var excessiveRefund = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{orderId}/refunds", new
        {
            idempotencyKey = $"sandbox-refund-{Guid.NewGuid():N}",
            amount = 20.00m
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, excessiveRefund.StatusCode);

        var save = await Send(client, shopperToken, HttpMethod.Post, "api/payment-methods", new
        {
            card = Card(cardNumber, cardCvc)
        });
        await AssertSuccessUnlessChallenge(save);
        var saved = await Json(save);
        var paymentMethodId = saved.RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.IsTrue(paymentMethodId > 0);
        var safeCard = saved.RootElement.GetProperty("paymentMethod");
        Assert.AreEqual("1111", safeCard.GetProperty("lastDigits").GetString());

        var otherShopperToken = ApiTokenHelper.GetUserToken("other-shopper@example.test");
        var otherOrderId = await PlaceOrder(client, otherShopperToken, 3);
        var crossShopperPay = await Send(client, otherShopperToken, HttpMethod.Post, $"api/orders/{otherOrderId}/pay", new
        {
            paymentMethodId
        });
        Assert.AreEqual(HttpStatusCode.NotFound, crossShopperPay.StatusCode);
        var crossShopperDelete = await Send(client, otherShopperToken, HttpMethod.Delete,
            $"api/payment-methods/{paymentMethodId}", null);
        Assert.AreEqual(HttpStatusCode.NotFound, crossShopperDelete.StatusCode);

        var secondOrderId = await PlaceOrder(client, shopperToken, 2);
        var savedPay = await Send(client, shopperToken, HttpMethod.Post, $"api/orders/{secondOrderId}/pay", new
        {
            paymentMethodId
        });
        await AssertSuccessUnlessChallenge(savedPay);
        var paidWithSaved = await Json(savedPay);
        Assert.AreEqual("Authorized", paidWithSaved.RootElement.GetProperty("paymentStatus").GetString());

        var cancel = await Send(client, adminToken, HttpMethod.Post, $"api/orders/{secondOrderId}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        var cancelled = await Json(cancel);
        Assert.AreEqual("Cancelled", cancelled.RootElement.GetProperty("fulfilmentStatus").GetString());
        Assert.AreEqual("Voided", cancelled.RootElement.GetProperty("paymentStatus").GetString());

        var delete = await Send(client, shopperToken, HttpMethod.Delete, $"api/payment-methods/{paymentMethodId}", null);
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var list = await Send(client, shopperToken, HttpMethod.Get, "api/payment-methods", null);
        list.EnsureSuccessStatusCode();
        var listed = await Json(list);
        Assert.IsFalse(listed.RootElement.EnumerateArray().Any(item =>
            item.GetProperty("paymentMethodId").GetInt32() == paymentMethodId));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await Send(client, adminToken, HttpMethod.Get, $"api/reconciliation?from={from}&to={to}", null);
        await AssertSuccessUnlessChallenge(reconciliation);
    }

    private static object Card(string number, string cvc) => new
    {
        name = "Sandbox Shopper",
        number,
        expiry = "2030-12",
        securityCode = cvc,
        billingAddress = new
        {
            addressLine1 = "123 Main Street",
            adminArea2 = "San Jose",
            adminArea1 = "CA",
            postalCode = "95131",
            countryCode = "US"
        }
    };

    private static async Task<int> PlaceOrder(HttpClient client, string token, int catalogItemId)
    {
        var response = await Send(client, token, HttpMethod.Post, "api/orders", new
        {
            items = new[] { new { catalogItemId, quantity = 1 } },
            shipToAddress = new
            {
                street = "123 Main Street",
                city = "San Jose",
                state = "CA",
                country = "US",
                zipCode = "95131"
            }
        });
        response.EnsureSuccessStatusCode();
        var json = await Json(response);
        return json.RootElement.GetProperty("orderId").GetInt32();
    }

    private static async Task<string> Authenticate(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("api/authenticate", new { username, password });
        response.EnsureSuccessStatusCode();
        var json = await Json(response);
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string token, HttpMethod method,
        string uri, object? body)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task AssertSuccessUnlessChallenge(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync();
        if (text.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("PayPal returned PAYER_ACTION_REQUIRED. Sandbox verification stopped; no approval round-trip was built.");
        Assert.Fail($"Sandbox call failed with HTTP {(int)response.StatusCode}: {text}");
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new AssertFailedException($"{name} is required for sandbox verification.");

    private static void SetConfigurationEnvironment(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value);
}
