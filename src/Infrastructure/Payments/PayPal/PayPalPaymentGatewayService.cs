using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGatewayService"/> over the REST APIs:
/// Orders v2 (create + capture), Payments v2 (refund) and Vault v3 (save / delete cards).
/// Card data is only ever placed into outbound request bodies; it is never logged or persisted.
/// </summary>
public class PayPalPaymentGatewayService : IPaymentGatewayService
{
    private const string CompletedStatus = "COMPLETED";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalPaymentGatewayService> _logger;

    public PayPalPaymentGatewayService(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        ILogger<PayPalPaymentGatewayService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<CardPaymentResult> ChargeCardAsync(CardChargeRequest request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = request.Currency, value = FormatAmount(request.Amount) } }
            },
            payment_source = new { card = BuildCardPayload(request.Card) }
        };

        using var document = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, cancellationToken);
        return ReadCapture(document.RootElement);
    }

    public async Task<CardPaymentResult> ChargeSavedCardAsync(SavedCardChargeRequest request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = request.Currency, value = FormatAmount(request.Amount) } }
            },
            payment_source = new { card = new { vault_id = request.VaultId } }
        };

        using var document = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, cancellationToken);
        return ReadCapture(document.RootElement);
    }

    public async Task<RefundResult> RefundAsync(string captureId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureId))
        {
            throw new PaymentGatewayException("A capture id is required to issue a refund.");
        }

        // Empty body => full refund of the capture.
        using var document = await SendAsync(
            HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", new { }, cancellationToken);

        var root = document.RootElement;
        var status = GetString(root, "status") ?? string.Empty;
        var refundId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal refund response did not contain a refund id.");

        if (!string.Equals(status, CompletedStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException($"PayPal refund was not completed (status '{status}').");
        }

        _logger.LogInformation("PayPal refund {RefundId} for capture {CaptureId} status {Status}.", refundId, captureId, status);
        return new RefundResult(refundId, status);
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card.
        var setupBody = new { payment_source = new { card = BuildCardPayload(card) } };
        string setupTokenId;
        using (var setupDocument = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody, cancellationToken))
        {
            setupTokenId = GetString(setupDocument.RootElement, "id")
                ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");
        }

        // Step 2: exchange the setup token for a permanent payment (vault) token.
        var tokenBody = new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } };
        using var tokenDocument = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody, cancellationToken);

        var root = tokenDocument.RootElement;
        var paymentTokenId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal did not return a payment token id.");
        var customerId = root.TryGetProperty("customer", out var customer) ? GetString(customer, "id") : null;

        string brand = "CARD";
        string last4 = "0000";
        string expiry = string.Empty;
        string? name = null;
        if (root.TryGetProperty("payment_source", out var paymentSource)
            && paymentSource.TryGetProperty("card", out var cardElement))
        {
            brand = GetString(cardElement, "brand") ?? brand;
            last4 = GetString(cardElement, "last_digits") ?? last4;
            expiry = GetString(cardElement, "expiry") ?? card.Expiry;
            name = GetString(cardElement, "name");
        }

        _logger.LogInformation("Vaulted a {Brand} card ending {Last4} as payment token {TokenId}.", brand, last4, paymentTokenId);
        return new VaultedCard(paymentTokenId, brand, last4, expiry, name ?? card.CardholderName, customerId);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentTokenId))
        {
            throw new PaymentGatewayException("A payment token id is required to delete a saved card.");
        }

        var client = _httpClientFactory.CreateClient(PayPalConstants.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}");
        await AuthorizeAsync(request, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        // 204 => deleted; 404 => already gone. Both leave the card unusable, which is the goal.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Deleted PayPal payment token {TokenId} (status {StatusCode}).", paymentTokenId, (int)response.StatusCode);
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw BuildGatewayException("delete a saved card", response.StatusCode, errorBody);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static object BuildCardPayload(CardDetails card) => new
    {
        number = card.Number,
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        name = card.CardholderName,
        billing_address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        }
    };

    private CardPaymentResult ReadCapture(JsonElement root)
    {
        var orderId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal order response did not contain an order id.");
        var orderStatus = GetString(root, "status") ?? string.Empty;

        if (!string.Equals(orderStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase))
        {
            // e.g. PAYER_ACTION_REQUIRED (3DS challenge) or a declined card.
            throw new PaymentGatewayException(
                $"PayPal did not complete the payment (order status '{orderStatus}'). " +
                "The card may have been declined or require additional authentication.");
        }

        if (root.TryGetProperty("purchase_units", out var units)
            && units.ValueKind == JsonValueKind.Array
            && units.GetArrayLength() > 0
            && units[0].TryGetProperty("payments", out var payments)
            && payments.TryGetProperty("captures", out var captures)
            && captures.ValueKind == JsonValueKind.Array
            && captures.GetArrayLength() > 0)
        {
            var capture = captures[0];
            var captureId = GetString(capture, "id")
                ?? throw new PaymentGatewayException("PayPal capture did not contain a capture id.");
            var captureStatus = GetString(capture, "status") ?? orderStatus;

            _logger.LogInformation("PayPal captured order {OrderId} (capture {CaptureId}, status {Status}).", orderId, captureId, captureStatus);
            return new CardPaymentResult(orderId, captureId, captureStatus);
        }

        throw new PaymentGatewayException("PayPal order completed but no capture was returned.");
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalConstants.HttpClientName);

        using var request = new HttpRequestMessage(method, path);
        await AuthorizeAsync(request, cancellationToken);
        // A fresh request id gives PayPal-side protection against accidental network-level retries of
        // this single attempt. Effect-level idempotency for double-clicks is enforced by the caller.
        request.Headers.Add("PayPal-Request-Id", Guid.NewGuid().ToString());
        request.Headers.Add("Prefer", "return=representation");

        var json = JsonSerializer.Serialize(body, SerializerOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException(DescribeOperation(path), response.StatusCode, responseBody);
        }

        return string.IsNullOrWhiteSpace(responseBody)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(responseBody);
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    private PaymentGatewayException BuildGatewayException(string operation, HttpStatusCode statusCode, string responseBody)
    {
        // Extract PayPal's structured error (name/message/issue) without leaking anything sensitive.
        string? name = null;
        string? message = null;
        string? issue = null;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            if (root.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                issue = GetString(details[0], "issue");
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; fall back to the status code only.
        }

        var detail = issue is not null ? $"{name}: {issue}" : name ?? message;
        _logger.LogError("PayPal failed to {Operation}: status {StatusCode}, {Name}/{Issue}.", operation, (int)statusCode, name, issue);
        return new PaymentGatewayException(
            $"PayPal failed to {operation} (status {(int)statusCode}){(detail is null ? "." : $": {detail}.")}");
    }

    private static string DescribeOperation(string path)
    {
        if (path.Contains("refund", StringComparison.OrdinalIgnoreCase)) return "process the refund";
        if (path.Contains("setup-tokens", StringComparison.OrdinalIgnoreCase)) return "save the card";
        if (path.Contains("payment-tokens", StringComparison.OrdinalIgnoreCase)) return "save the card";
        if (path.Contains("orders", StringComparison.OrdinalIgnoreCase)) return "process the payment";
        return "complete the payment operation";
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
