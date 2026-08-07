using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Talks to the PayPal REST API exactly as described by the OpenAPI specs under
/// <c>api-specs/paypal/</c>:
/// <list type="bullet">
/// <item>Checkout Orders v2 — POST /v2/checkout/orders (create + capture with a card / vault id)</item>
/// <item>Payments v2 — POST /v2/payments/captures/{capture_id}/refund (full refund)</item>
/// <item>Vault v3 — POST/DELETE /v3/vault/payment-tokens (save / remove a card)</item>
/// </list>
/// This is the only type that speaks HTTP to PayPal. Raw card details are forwarded to PayPal
/// but never persisted or logged.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private const string CreateOrderPath = "v2/checkout/orders";
    private const string VaultTokensPath = "v3/vault/payment-tokens";
    private static string RefundPath(string captureId) => $"v2/payments/captures/{captureId}/refund";
    private static string DeleteTokenPath(string vaultId) => $"{VaultTokensPath}/{vaultId}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IAppLogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<CaptureResult> CreateAndCaptureOrderAsync(
        decimal amount,
        string currencyCode,
        CardPaymentSource source,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var card = new PayPalCardRequest();
        if (source.VaultId is not null)
        {
            card.VaultId = source.VaultId;
        }
        else if (source.Card is not null)
        {
            card.Name = source.Card.Name;
            card.Number = source.Card.Number;
            card.Expiry = source.Card.Expiry;
            card.SecurityCode = source.Card.SecurityCode;
            card.BillingAddress = BuildAddress(source.Card);
        }
        else
        {
            throw new PaymentProcessingException("A card or a saved-card vault id is required to pay.");
        }

        var request = new PayPalCreateOrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    Amount = new PayPalMoney
                    {
                        CurrencyCode = currencyCode,
                        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = new PayPalPaymentSourceRequest { Card = card }
        };

        // Prefer=return=representation so the response carries the captures inline.
        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post, CreateOrderPath, request, idempotencyKey,
            preferRepresentation: true, cancellationToken);

        if (order?.Id is null)
        {
            throw new PaymentProcessingException("PayPal did not return an order id.");
        }

        var capture = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Captures?
            .FirstOrDefault();

        return new CaptureResult
        {
            PayPalOrderId = order.Id,
            OrderStatus = order.Status ?? "UNKNOWN",
            CaptureId = capture?.Id,
            CaptureStatus = capture?.Status
        };
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // An empty body refunds the full captured amount (spec: refund_request.amount optional).
        var refund = await SendAsync<PayPalRefundResponse>(
            HttpMethod.Post, RefundPath(captureId), new { }, idempotencyKey,
            preferRepresentation: true, cancellationToken);

        if (refund?.Id is null)
        {
            throw new PaymentProcessingException("PayPal did not return a refund id.");
        }

        return new RefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status ?? "UNKNOWN"
        };
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string? customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreatePaymentTokenRequest
        {
            Customer = string.IsNullOrEmpty(customerId) ? null : new PayPalVaultCustomer { Id = customerId },
            PaymentSource = new PayPalVaultPaymentSourceRequest
            {
                Card = new PayPalVaultCardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post, VaultTokensPath, request, idempotencyKey,
            preferRepresentation: false, cancellationToken);

        if (token?.Id is null)
        {
            throw new PaymentProcessingException("PayPal did not return a vault token id.");
        }

        var responseCard = token.PaymentSource?.Card;
        return new VaultedCardResult
        {
            VaultId = token.Id,
            CustomerId = token.Customer?.Id ?? customerId ?? string.Empty,
            CardBrand = responseCard?.Brand,
            LastDigits = responseCard?.LastDigits,
            Expiry = responseCard?.Expiry ?? card.Expiry,
            CardholderName = responseCard?.Name ?? card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        using var request = await BuildAuthorizedRequestAsync(
            HttpMethod.Delete, DeleteTokenPath(vaultId), idempotencyKey: null, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);

        // 204 = deleted; 404 = already gone. Both leave the card unusable, so both are success.
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = PayPalErrorReader.Parse(body);
        _logger.LogWarning("PayPal vault delete failed ({0}): {1}", (int)response.StatusCode, error.Message);
        throw new PaymentProcessingException(
            $"Could not delete the saved card from PayPal: {error.Message}", error.DebugId);
    }

    // -- helpers -------------------------------------------------------------

    private static PayPalAddress? BuildAddress(CardDetails card)
    {
        var hasAny = !string.IsNullOrWhiteSpace(card.BillingAddressLine1)
            || !string.IsNullOrWhiteSpace(card.BillingAdminArea2)
            || !string.IsNullOrWhiteSpace(card.BillingAdminArea1)
            || !string.IsNullOrWhiteSpace(card.BillingPostalCode)
            || !string.IsNullOrWhiteSpace(card.BillingCountryCode);

        if (!hasAny)
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingAdminArea2,
            AdminArea1 = card.BillingAdminArea1,
            PostalCode = card.BillingPostalCode,
            // country_code is required whenever a billing address is present.
            CountryCode = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode
        };
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object body,
        string? idempotencyKey,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        using var request = await BuildAuthorizedRequestAsync(method, path, idempotencyKey, cancellationToken);

        if (preferRepresentation)
        {
            request.Headers.Add("Prefer", "return=representation");
        }

        var json = PayPalJson.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = PayPalErrorReader.Parse(responseBody);
            _logger.LogWarning("PayPal call {0} {1} failed ({2}): {3} [debug_id={4}]",
                method.Method, path, (int)response.StatusCode, error.Message, error.DebugId ?? "n/a");
            throw new PaymentProcessingException(
                $"PayPal request failed: {error.Message}", error.DebugId);
        }

        return PayPalJson.Deserialize<TResponse>(responseBody);
    }

    private async Task<HttpRequestMessage> BuildAuthorizedRequestAsync(
        HttpMethod method,
        string path,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        }

        return request;
    }
}
