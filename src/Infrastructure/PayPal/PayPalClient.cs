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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTP PayPal REST client (Orders v2, Payments v2, Payment Method Tokens v3,
/// Transaction Search v1). Card data flows through requests but is never logged
/// or persisted.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = BuildCardJson(card)
        };
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = new JsonObject
            {
                ["vault_id"] = vaultTokenId,
                ["stored_credential"] = new JsonObject
                {
                    ["payment_initiator"] = "CUSTOMER",
                    ["payment_type"] = "ONE_TIME",
                    ["usage"] = "SUBSEQUENT"
                }
            }
        };
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    private async Task<GatewayAuthorizationResult> CreateAndAuthorizeOrderAsync(decimal amount, string currency, JsonObject paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = FormatAmount(amount)
                    }
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody, idempotencyKey, cancellationToken);
        var orderId = order["id"]!.GetValue<string>();
        var orderStatus = order["status"]?.GetValue<string>();
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (payer action required), which this integration does not support.",
                (int)HttpStatusCode.UnprocessableEntity, order["debug_id"]?.GetValue<string>());
        }

        var authorized = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize", new JsonObject(), idempotencyKey + ":authorize", cancellationToken);
        var authorization = authorized["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw new PaymentGatewayException($"PayPal order {orderId} authorized but no authorization was returned.");

        return new GatewayAuthorizationResult
        {
            PayPalOrderId = orderId,
            AuthorizationId = authorization["id"]!.GetValue<string>(),
            Status = authorization["status"]!.GetValue<string>(),
            Amount = ParseAmount(authorization["amount"]),
            Currency = authorization["amount"]?["currency_code"]?.GetValue<string>() ?? currency,
            ExpiresAt = ParseTimestamp(authorization["expiration_time"]?.GetValue<string>())
        };
    }

    public async Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = currency,
                ["value"] = FormatAmount(amount)
            },
            ["final_capture"] = true
        };

        var capture = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);
        var breakdown = capture["seller_receivable_breakdown"];
        return new GatewayCaptureResult
        {
            CaptureId = capture["id"]!.GetValue<string>(),
            Status = capture["status"]!.GetValue<string>(),
            Amount = ParseAmount(capture["amount"]),
            Fee = ParseAmount(breakdown?["paypal_fee"]),
            NetAmount = ParseAmount(breakdown?["net_amount"]),
            Currency = capture["amount"]?["currency_code"]?.GetValue<string>() ?? currency
        };
    }

    public async Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = currency,
                ["value"] = FormatAmount(amount)
            }
        };

        var authorization = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, null, cancellationToken);
        return new GatewayAuthorizationResult
        {
            AuthorizationId = authorization["id"]!.GetValue<string>(),
            Status = authorization["status"]!.GetValue<string>(),
            Amount = ParseAmount(authorization["amount"]),
            Currency = authorization["amount"]?["currency_code"]?.GetValue<string>() ?? currency,
            ExpiresAt = ParseTimestamp(authorization["expiration_time"]?.GetValue<string>())
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.IsNotFound || ex.IsAuthorizationUnusable)
        {
            // Already voided or no longer voidable - the funds are not held either way.
            _logger.LogInformation("Authorization {AuthorizationId} was already void/unusable at PayPal.", authorizationId);
        }
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        JsonObject? body = null;
        if (amount.HasValue)
        {
            body = new JsonObject
            {
                ["amount"] = new JsonObject
                {
                    ["currency_code"] = currency,
                    ["value"] = FormatAmount(amount.Value)
                }
            };
        }

        var refund = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        return new GatewayRefundResult
        {
            RefundId = refund["id"]!.GetValue<string>(),
            Status = refund["status"]!.GetValue<string>(),
            Amount = amount ?? ParseAmount(refund["amount"]),
            Currency = refund["amount"]?["currency_code"]?.GetValue<string>() ?? currency
        };
    }

    public async Task<GatewayVaultResult> VaultCardAsync(CardDetails card, string? payPalCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCardJson(card)
            }
        };
        if (!string.IsNullOrEmpty(payPalCustomerId))
        {
            setupBody["customer"] = new JsonObject { ["id"] = payPalCustomerId };
        }

        var setupToken = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, idempotencyKey + ":setup", cancellationToken);
        var setupTokenId = setupToken["id"]!.GetValue<string>();
        var customerId = setupToken["customer"]?["id"]?.GetValue<string>() ?? payPalCustomerId;

        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };
        var paymentToken = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, idempotencyKey + ":token", cancellationToken);

        var vaultedCard = paymentToken["payment_source"]?["card"];
        var expiry = vaultedCard?["expiry"]?.GetValue<string>() ?? string.Empty; // "YYYY-MM"
        var expiryParts = expiry.Split('-');
        return new GatewayVaultResult
        {
            VaultTokenId = paymentToken["id"]!.GetValue<string>(),
            PayPalCustomerId = paymentToken["customer"]?["id"]?.GetValue<string>() ?? customerId,
            Brand = vaultedCard?["brand"]?.GetValue<string>() ?? "CARD",
            LastDigits = vaultedCard?["last_digits"]?.GetValue<string>() ?? string.Empty,
            ExpiryYear = expiryParts.Length == 2 ? expiryParts[0] : string.Empty,
            ExpiryMonth = expiryParts.Length == 2 ? expiryParts[1] : string.Empty
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var path = "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatInstant(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatInstant(to))}" +
                $"&fields=transaction_info&page_size={pageSize}&page={page}";

            var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
            totalPages = response["total_pages"]?.GetValue<int>() ?? 1;

            foreach (var detail in response["transaction_details"]?.AsArray() ?? new JsonArray())
            {
                var info = detail?["transaction_info"];
                if (info == null) continue;
                results.Add(new GatewayTransaction
                {
                    TransactionId = info["transaction_id"]?.GetValue<string>() ?? string.Empty,
                    ReferenceId = info["paypal_reference_id"]?.GetValue<string>(),
                    ReferenceIdType = info["paypal_reference_id_type"]?.GetValue<string>(),
                    EventCode = info["transaction_event_code"]?.GetValue<string>(),
                    Status = info["transaction_status"]?.GetValue<string>() ?? string.Empty,
                    Amount = ParseAmount(info["transaction_amount"]),
                    Fee = info["fee_amount"] == null ? null : ParseAmount(info["fee_amount"]),
                    Currency = info["transaction_amount"]?["currency_code"]?.GetValue<string>() ?? string.Empty,
                    InitiatedAt = ParseTimestamp(info["transaction_initiation_date"]?.GetValue<string>()) ?? from,
                    UpdatedAt = ParseTimestamp(info["transaction_updated_date"]?.GetValue<string>()) ?? from
                });
            }

            page++;
        }

        return results;
    }

    private static JsonObject BuildCardJson(CardDetails card)
    {
        var json = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.ExpiryForGateway(),
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName
        };
        if (!string.IsNullOrEmpty(card.BillingCountryCode))
        {
            json["billing_address"] = new JsonObject
            {
                ["address_line_1"] = card.BillingAddressLine1,
                ["admin_area_2"] = card.BillingCity,
                ["admin_area_1"] = card.BillingState,
                ["postal_code"] = card.BillingPostalCode,
                ["country_code"] = card.BillingCountryCode
            };
        }
        return json;
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _settings.ResolveBaseUrl() + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        // NOTE: never log the request body - it can contain card data.
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }
        return JsonNode.Parse(content)!.AsObject();
    }

    private PaymentGatewayException ToGatewayException(HttpStatusCode statusCode, string content)
    {
        string? debugId = null;
        var message = $"PayPal request failed with status {(int)statusCode}.";
        var issues = new List<string>();
        try
        {
            var error = JsonNode.Parse(content)?.AsObject();
            debugId = error?["debug_id"]?.GetValue<string>();
            var name = error?["name"]?.GetValue<string>();
            var detail = error?["message"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(name)) message = $"PayPal error {name}: {detail}";
            foreach (var d in error?["details"]?.AsArray() ?? new JsonArray())
            {
                var issue = d?["issue"]?.GetValue<string>();
                var description = d?["description"]?.GetValue<string>();
                if (issue != null) issues.Add(issue + (description != null ? $" ({description})" : string.Empty));
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        var issueNames = string.Join("; ", issues);
        var fullMessage = string.IsNullOrEmpty(issueNames) ? message : $"{message} Issues: {issueNames}";
        if (debugId != null) fullMessage += $" [debug_id: {debugId}]";

        var unusableAuthorization = issues.Any(i =>
            i.StartsWith("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            i.StartsWith("INVALID_AUTHORIZATION", StringComparison.OrdinalIgnoreCase) ||
            i.StartsWith("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            i.StartsWith("AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase));

        _logger.LogWarning("PayPal call failed: {Message}", fullMessage);
        return new PaymentGatewayException(fullMessage, (int)statusCode, debugId,
            isAuthorizationUnusable: unusableAuthorization,
            isNotFound: statusCode == HttpStatusCode.NotFound);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new PaymentGatewayException("PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret configuration values.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.ResolveBaseUrl() + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
                throw new PaymentGatewayException($"PayPal authentication failed with status {(int)response.StatusCode}.", (int)response.StatusCode);
            }

            var token = JsonNode.Parse(content)!.AsObject();
            _accessToken = token["access_token"]!.GetValue<string>();
            var expiresIn = token["expires_in"]?.GetValue<int>() ?? 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(JsonNode? moneyNode) =>
        decimal.TryParse(moneyNode?["value"]?.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    }
}
