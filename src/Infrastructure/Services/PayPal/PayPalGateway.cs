using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST client covering OAuth2 tokens, Orders v2, Payments v2, Vault v3
/// and Transaction Search v1. Request bodies are built with JsonNode so that
/// PayPal's snake_case contract is explicit. Request payloads are never logged,
/// so full card details cannot end up in logs.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return _settings.BaseUrl!.TrimEnd('/');
            }
            return string.Equals(_settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;
        }
    }

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = referenceId,
                    ["custom_id"] = referenceId,
                    ["invoice_id"] = $"ESHOP-ORDER-{referenceId}-{Guid.NewGuid():N}".Substring(0, 24),
                    ["amount"] = Money(amount, currency)
                }
            }
        };

        var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        return RequiredString(response, "id");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        PayPalCardDetails? card, string? vaultTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = BuildPaymentSource(card, vaultTokenId)
        };

        var response = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            body, idempotencyKey, cancellationToken, preferRepresentation: true);

        ThrowIfPayerActionRequired(response);

        var authorization = response?["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw new PayPalApiException(HttpStatusCode.OK, null, null,
                "PayPal did not return an authorization for the order.");

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = RequiredString(authorization, "id"),
            Status = OptionalString(authorization, "status") ?? string.Empty,
            Amount = ParseMoney(authorization["amount"]),
            Currency = OptionalString(authorization["amount"], "currency_code") ?? string.Empty,
            ExpirationTime = ParseDate(authorization, "expiration_time")
        };
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, null, cancellationToken);
        return ParseAuthorization(response!);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken,
            preferRepresentation: true);
        return ParseAuthorization(response!);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId,
        decimal amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };

        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken,
            preferRepresentation: true);

        var breakdown = response?["seller_receivable_breakdown"];
        return new PayPalCaptureResult
        {
            CaptureId = RequiredString(response, "id"),
            Status = OptionalString(response, "status") ?? string.Empty,
            GrossAmount = breakdown != null ? ParseMoney(breakdown["gross_amount"]) : ParseMoney(response?["amount"]),
            PayPalFee = breakdown?["paypal_fee"] != null ? ParseMoney(breakdown["paypal_fee"]) : null,
            NetAmount = breakdown?["net_amount"] != null ? ParseMoney(breakdown["net_amount"]) : null,
            Currency = OptionalString(response?["amount"], "currency_code") ?? currency
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            new JsonObject(), idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string? noteToPayer, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (amount.HasValue)
        {
            body["amount"] = Money(amount.Value, currency);
        }
        if (!string.IsNullOrEmpty(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, cancellationToken, preferRepresentation: true);

        return new PayPalRefundResult
        {
            RefundId = RequiredString(response, "id"),
            Status = OptionalString(response, "status") ?? string.Empty,
            Amount = ParseMoney(response?["amount"]),
            Currency = OptionalString(response?["amount"], "currency_code") ?? currency
        };
    }

    public async Task<PayPalVaultedCard> SaveCardAsync(string customerId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCard(card)
            }
        };
        var setupToken = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            setupBody, idempotencyKey, cancellationToken);

        var setupStatus = OptionalString(setupToken, "status");
        if (setupStatus != null && setupStatus != "APPROVED")
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal requires a browser approval to save this card (setup token status {setupStatus}). " +
                "This integration is API-only and does not implement an approval round-trip.");
        }

        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = RequiredString(setupToken, "id"),
                    ["type"] = "SETUP_TOKEN"
                }
            },
            ["customer"] = new JsonObject
            {
                ["id"] = ToPayPalCustomerId(customerId)
            }
        };
        var paymentToken = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            tokenBody, idempotencyKey, cancellationToken);

        var cardNode = paymentToken?["payment_source"]?["card"];
        return new PayPalVaultedCard
        {
            VaultTokenId = RequiredString(paymentToken, "id"),
            Brand = OptionalString(cardNode, "brand"),
            LastDigits = OptionalString(cardNode, "last_digits"),
            Expiry = OptionalString(cardNode, "expiry"),
            CardholderName = OptionalString(cardNode, "name")
        };
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}",
            null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = new List<PayPalTransaction>();

        // Transaction Search supports a maximum 31-day window per request.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            var page = 1;
            while (true)
            {
                var query = $"/v1/reporting/transactions?start_date={FormatDate(windowStart)}" +
                            $"&end_date={FormatDate(windowEnd)}&fields=all&page_size=100&page={page}&total_required=true";
                var response = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);

                var details = response?["transaction_details"] as JsonArray;
                if (details != null)
                {
                    foreach (var detail in details)
                    {
                        var info = detail?["transaction_info"];
                        if (info == null) continue;
                        transactions.Add(new PayPalTransaction
                        {
                            TransactionId = OptionalString(info, "transaction_id") ?? string.Empty,
                            ReferenceId = OptionalString(info, "paypal_reference_id"),
                            ReferenceIdType = OptionalString(info, "paypal_reference_id_type"),
                            EventCode = OptionalString(info, "transaction_event_code"),
                            Status = OptionalString(info, "transaction_status"),
                            Amount = ParseMoney(info["transaction_amount"]),
                            Currency = OptionalString(info["transaction_amount"], "currency_code") ?? string.Empty,
                            Fee = info["fee_amount"] != null ? ParseMoney(info["fee_amount"]) : null,
                            InitiationDate = ParseDate(info, "transaction_initiation_date"),
                            UpdatedDate = ParseDate(info, "transaction_updated_date"),
                            InvoiceId = OptionalString(info, "invoice_id"),
                            CustomField = OptionalString(info, "custom_field")
                        });
                    }
                }

                var totalPages = response?["total_pages"]?.GetValue<int>() ?? 1;
                if (page >= totalPages || details == null || details.Count == 0)
                {
                    break;
                }
                page++;
            }
            windowStart = windowEnd;
        }

        return transactions;
    }

    private static JsonObject BuildPaymentSource(PayPalCardDetails? card, string? vaultTokenId)
    {
        if (!string.IsNullOrEmpty(vaultTokenId))
        {
            return new JsonObject
            {
                ["card"] = new JsonObject
                {
                    ["vault_id"] = vaultTokenId,
                    ["stored_credential"] = new JsonObject
                    {
                        ["payment_initiator"] = "CUSTOMER",
                        ["payment_type"] = "ONE_TIME"
                    }
                }
            };
        }

        return new JsonObject
        {
            ["card"] = BuildCard(card!)
        };
    }

    private static JsonObject BuildCard(PayPalCardDetails card)
    {
        var cardNode = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode))
        {
            cardNode["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrEmpty(card.CardholderName))
        {
            cardNode["name"] = card.CardholderName;
        }
        if (card.BillingAddress != null)
        {
            var address = new JsonObject
            {
                ["country_code"] = card.BillingAddress.CountryCode
            };
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine1)) address["address_line_1"] = card.BillingAddress.AddressLine1;
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine2)) address["address_line_2"] = card.BillingAddress.AddressLine2;
            if (!string.IsNullOrEmpty(card.BillingAddress.AdminArea2)) address["admin_area_2"] = card.BillingAddress.AdminArea2;
            if (!string.IsNullOrEmpty(card.BillingAddress.AdminArea1)) address["admin_area_1"] = card.BillingAddress.AdminArea1;
            if (!string.IsNullOrEmpty(card.BillingAddress.PostalCode)) address["postal_code"] = card.BillingAddress.PostalCode;
            cardNode["billing_address"] = address;
        }
        return cardNode;
    }

    private static void ThrowIfPayerActionRequired(JsonNode? response)
    {
        var status = OptionalString(response, "status");
        var links = response?["links"] as JsonArray;
        if (links != null)
        {
            foreach (var link in links)
            {
                var rel = OptionalString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentChallengeRequiredException(
                        "PayPal requires the shopper to approve this card payment in a browser " +
                        "(payer-action challenge). This integration is API-only and does not implement an approval round-trip.");
                }
            }
        }
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser. " +
                "This integration is API-only and does not implement an approval round-trip.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (user-secrets or environment).");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(response.StatusCode, null, null,
                    $"PayPal token request failed with status {(int)response.StatusCode}.");
            }

            var token = JsonNode.Parse(payload);
            _accessToken = RequiredString(token, "access_token");
            var expiresIn = token?["expires_in"]?.GetValue<int>() ?? 300;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body,
        string? idempotencyKey, CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string? errorName = null;
            string? debugId = null;
            string? message = null;
            try
            {
                var error = JsonNode.Parse(payload);
                errorName = OptionalString(error, "name");
                debugId = OptionalString(error, "debug_id");
                message = OptionalString(error, "message");
                var details = error?["details"] as JsonArray;
                if (details != null)
                {
                    var issues = details
                        .Select(d => $"{OptionalString(d, "field")}: {OptionalString(d, "issue")} {OptionalString(d, "description")}")
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    var joined = string.Join("; ", issues);
                    if (!string.IsNullOrEmpty(joined))
                    {
                        message = $"{message} [{joined}]";
                    }
                }
            }
            catch (JsonException) { }

            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {ErrorName} {DebugId}",
                method, path, (int)response.StatusCode, errorName, debugId);
            throw new PayPalApiException(response.StatusCode, errorName, debugId,
                message ?? $"PayPal request {method} {path} failed with status {(int)response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }
        return JsonNode.Parse(payload);
    }

    // Vault customer.id must match ^[0-9a-zA-Z_-]+$ and be at most 22 chars;
    // derive a deterministic, safe id from the buyer identity (an email here).
    private static string ToPayPalCustomerId(string buyerId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return $"eshop-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static JsonObject Money(decimal amount, string currency) => new JsonObject
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string RequiredString(JsonNode? node, string property) =>
        node?[property]?.GetValue<string>()
        ?? throw new PayPalApiException(HttpStatusCode.OK, null, null,
            $"PayPal response did not include the expected '{property}' field.");

    private static string? OptionalString(JsonNode? node, string property) =>
        node?[property]?.GetValue<string>();

    private static decimal ParseMoney(JsonNode? money)
    {
        var value = OptionalString(money, "value");
        return value != null ? decimal.Parse(value, CultureInfo.InvariantCulture) : 0m;
    }

    private static DateTimeOffset? ParseDate(JsonNode? node, string property)
    {
        var value = OptionalString(node, property);
        return value != null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static PayPalAuthorizationDetails ParseAuthorization(JsonNode node) => new PayPalAuthorizationDetails
    {
        AuthorizationId = RequiredString(node, "id"),
        Status = OptionalString(node, "status") ?? string.Empty,
        Amount = ParseMoney(node["amount"]),
        Currency = OptionalString(node["amount"], "currency_code") ?? string.Empty,
        ExpirationTime = ParseDate(node, "expiration_time")
    };
}
