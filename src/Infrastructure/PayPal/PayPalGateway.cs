using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal integration: OAuth token management, order authorize/capture/void/refund,
/// card vaulting, and transaction-search reconciliation. Raw card data is only ever forwarded to
/// PayPal — it is never logged and never persisted.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // PayPal Transaction Search allows a maximum window of 31 days per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _settings.Validate();
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => _settings.Currency!;

    // ----------------------------------------------------------------------------------------
    // OAuth
    // ----------------------------------------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException("Failed to obtain a PayPal access token", response.StatusCode, body);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            _cachedToken = token;
            _logger.LogInformation("Obtained a PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ----------------------------------------------------------------------------------------
    // Core request helper
    // ----------------------------------------------------------------------------------------

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, bool preferRepresentation, CancellationToken ct, string action)
    {
        // One transparent retry if the cached token was rejected (401).
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // Force a token refresh and retry once.
                _cachedToken = null;
                _tokenExpiresAt = DateTimeOffset.MinValue;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildException($"PayPal {action} failed", response.StatusCode, responseBody);
            }

            _logger.LogInformation("PayPal {Action} succeeded ({StatusCode}).", action, (int)response.StatusCode);
            return string.IsNullOrWhiteSpace(responseBody) ? null : JsonNode.Parse(responseBody);
        }
    }

    private static PayPalApiException BuildException(string context, HttpStatusCode status, string body)
    {
        string? name = null, issue = null, debugId = null, message = null;
        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonObject obj)
            {
                name = obj["name"]?.GetValue<string>() ?? obj["error"]?.GetValue<string>();
                message = obj["message"]?.GetValue<string>() ?? obj["error_description"]?.GetValue<string>();
                debugId = obj["debug_id"]?.GetValue<string>();
                if (obj["details"] is JsonArray details && details.Count > 0 && details[0] is JsonObject d0)
                {
                    issue = d0["issue"]?.GetValue<string>();
                    var desc = d0["description"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(desc)) message = $"{message} ({issue}: {desc})";
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with raw text.
        }

        var full = $"{context}: HTTP {(int)status} {name ?? "error"}" +
                   (message is not null ? $" - {message}" : string.Empty) +
                   (debugId is not null ? $" [debug_id={debugId}]" : string.Empty);
        return new PayPalApiException(full, (int)status, name, issue, debugId);
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private JsonObject MoneyNode(decimal amount) => new()
    {
        ["currency_code"] = Currency,
        ["value"] = FormatMoney(amount)
    };

    private static decimal? ReadMoney(JsonNode? money)
    {
        var value = money?["value"]?.GetValue<string>();
        return value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadDate(JsonNode? node)
    {
        var value = node?.GetValue<string>();
        return value is null ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private JsonObject BuildCardNode(PayPalCardDetails card)
    {
        var billing = new JsonObject { ["country_code"] = card.CountryCode };
        if (!string.IsNullOrEmpty(card.BillingAddressLine1)) billing["address_line_1"] = card.BillingAddressLine1;
        if (!string.IsNullOrEmpty(card.BillingAddressLine2)) billing["address_line_2"] = card.BillingAddressLine2;
        if (!string.IsNullOrEmpty(card.AdminArea2)) billing["admin_area_2"] = card.AdminArea2;
        if (!string.IsNullOrEmpty(card.AdminArea1)) billing["admin_area_1"] = card.AdminArea1;
        if (!string.IsNullOrEmpty(card.PostalCode)) billing["postal_code"] = card.PostalCode;

        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["billing_address"] = billing
        };
        if (!string.IsNullOrEmpty(card.Name)) node["name"] = card.Name;
        return node;
    }

    // ----------------------------------------------------------------------------------------
    // Authorize (create order + authorize)
    // ----------------------------------------------------------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken ct = default)
    {
        if (request.Card is null && string.IsNullOrEmpty(request.VaultTokenId))
        {
            throw new PaymentValidationException("A card or a saved-card token is required to authorize a payment.");
        }

        // 1) Create the order with intent AUTHORIZE.
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.InvoiceId,
                    ["description"] = request.Description,
                    ["amount"] = MoneyNode(request.Amount)
                }
            }
        };

        var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody,
            requestId: $"{request.RequestId}-order", preferRepresentation: false, ct, "create order");
        var payPalOrderId = created?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return an order id.", 502, null, null, null);

        // 2) Authorize the order with the funding source (card or saved-card token).
        var cardNode = request.Card is not null
            ? BuildCardNode(request.Card)
            : new JsonObject { ["vault_id"] = request.VaultTokenId };

        var authBody = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardNode }
        };

        var authorized = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            authBody, requestId: $"{request.RequestId}-auth", preferRepresentation: true, ct, "authorize order");

        GuardAgainstChallenge(authorized, "authorize");

        var authorization = authorized?["purchase_units"]?[0]?["payments"]?["authorizations"]?[0];
        if (authorization is null)
        {
            var status = authorized?["status"]?.GetValue<string>();
            throw new PayPalApiException(
                $"PayPal did not return an authorization (order status '{status}').", 502, null, null, null);
        }

        var authId = authorization["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal authorization is missing an id.", 502, null, null, null);
        var authStatus = authorization["status"]?.GetValue<string>() ?? "CREATED";
        var expiresAt = ReadDate(authorization["expiration_time"]);
        var amount = ReadMoney(authorization["amount"]) ?? request.Amount;

        var cardResp = authorized?["payment_source"]?["card"];
        var brand = cardResp?["brand"]?.GetValue<string>();
        var last4 = cardResp?["last_digits"]?.GetValue<string>();

        return new PayPalAuthorizationResult(payPalOrderId, authId, authStatus, expiresAt, amount, brand, last4);
    }

    private void GuardAgainstChallenge(JsonNode? response, string action)
    {
        var status = response?["status"]?.GetValue<string>();
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires the shopper to approve this {action} in a browser (status PAYER_ACTION_REQUIRED). " +
                "This integration does not build an approval round-trip.");
        }

        if (response?["links"] is JsonArray links)
        {
            foreach (var link in links)
            {
                var rel = link?["rel"]?.GetValue<string>();
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayPalChallengeRequiredException(
                        $"PayPal returned a payer-action link for this {action}, indicating a browser approval " +
                        "(e.g. 3-D Secure challenge) is required. This integration does not build an approval round-trip.");
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    // Capture / Reauthorize / Void
    // ----------------------------------------------------------------------------------------

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        string requestId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["final_capture"] = true,
            ["invoice_id"] = invoiceId,
            ["amount"] = MoneyNode(amount)
        };

        var captured = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, preferRepresentation: true, ct, "capture authorization");

        var captureId = captured?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal capture is missing an id.", 502, null, null, null);
        var status = captured?["status"]?.GetValue<string>() ?? "COMPLETED";
        var capturedAmount = ReadMoney(captured?["amount"]) ?? amount;

        var breakdown = captured?["seller_receivable_breakdown"];
        var fee = ReadMoney(breakdown?["paypal_fee"]);
        var net = ReadMoney(breakdown?["net_amount"]);

        return new PayPalCaptureResult(captureId, status, capturedAmount, fee, net);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken ct = default)
    {
        var body = new JsonObject { ["amount"] = MoneyNode(amount) };

        var reauth = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, requestId, preferRepresentation: true, ct, "reauthorize authorization");

        var newAuthId = reauth?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal reauthorization is missing an id.", 502, null, null, null);
        var status = reauth?["status"]?.GetValue<string>() ?? "CREATED";
        var expiresAt = ReadDate(reauth?["expiration_time"]);
        var newAmount = ReadMoney(reauth?["amount"]) ?? amount;

        return new PayPalAuthorizationResult(string.Empty, newAuthId, status, expiresAt, newAmount, null, null);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            body: null, requestId, preferRepresentation: false, ct, "void authorization");
    }

    // ----------------------------------------------------------------------------------------
    // Refund
    // ----------------------------------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        var body = new JsonObject();
        if (amount is not null)
        {
            body["amount"] = MoneyNode(amount.Value);
        }

        var refund = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, requestId: idempotencyKey, preferRepresentation: true, ct, "refund capture");

        var refundId = refund?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal refund is missing an id.", 502, null, null, null);
        var status = refund?["status"]?.GetValue<string>() ?? "COMPLETED";
        var refundedAmount = ReadMoney(refund?["amount"]) ?? amount ?? 0m;

        return new PayPalRefundResult(refundId, status, refundedAmount);
    }

    // ----------------------------------------------------------------------------------------
    // Vault
    // ----------------------------------------------------------------------------------------

    public async Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardDetails card, string? existingCustomerId,
        string merchantCustomerId, string requestId, CancellationToken ct = default)
    {
        var customer = new JsonObject();
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            customer["id"] = existingCustomerId;
        }
        else
        {
            customer["merchant_customer_id"] = merchantCustomerId;
        }

        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card) },
            ["customer"] = customer
        };

        var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId, preferRepresentation: false, ct, "vault card");

        var tokenId = token?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return a vault token id.", 502, null, null, null);
        var customerId = token?["customer"]?["id"]?.GetValue<string>() ?? existingCustomerId ?? merchantCustomerId;

        var cardResp = token?["payment_source"]?["card"];
        var brand = cardResp?["brand"]?.GetValue<string>();
        var last4 = cardResp?["last_digits"]?.GetValue<string>();
        var expiry = cardResp?["expiry"]?.GetValue<string>();

        return new PayPalVaultedCardResult(tokenId, customerId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}",
            body: null, requestId: null, preferRepresentation: false, ct, "delete vaulted card");
    }

    // ----------------------------------------------------------------------------------------
    // Transaction search (reconciliation)
    // ----------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // Chunk the requested span into windows PayPal accepts (max 31 days), and page each to completion.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = $"/v1/reporting/transactions" +
                            $"?start_date={Uri.EscapeDataString(ToRfc3339(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(ToRfc3339(windowEnd))}" +
                            $"&fields=transaction_info&page_size=500&page={page}";

                var response = await SendAsync(HttpMethod.Get, query, body: null, requestId: null,
                    preferRepresentation: false, ct, "list transactions");

                totalPages = response?["total_pages"]?.GetValue<int>() ?? 1;

                if (response?["transaction_details"] is JsonArray details)
                {
                    foreach (var detail in details)
                    {
                        var info = detail?["transaction_info"];
                        if (info is null) continue;
                        var txnAmount = info["transaction_amount"];
                        results.Add(new PayPalTransaction(
                            TransactionId: info["transaction_id"]?.GetValue<string>() ?? string.Empty,
                            Status: info["transaction_status"]?.GetValue<string>(),
                            Amount: ReadMoney(txnAmount),
                            Currency: txnAmount?["currency_code"]?.GetValue<string>(),
                            InvoiceId: info["invoice_id"]?.GetValue<string>(),
                            CustomField: info["custom_field"]?.GetValue<string>(),
                            InitiationDate: ReadDate(info["transaction_initiation_date"]),
                            EventCode: info["transaction_event_code"]?.GetValue<string>()));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
