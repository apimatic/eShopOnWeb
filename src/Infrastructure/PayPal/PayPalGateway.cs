using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// HTTP implementation of <see cref="IPayPalGateway"/> against the PayPal REST API. Uses the
/// Orders v2 API for authorize, the Payments v2 API for capture/void/reauthorize/refund, the
/// Vault v3 API for saved cards, and the Transaction Search v1 API for reconciliation.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    public const string HttpClientName = "PayPal";

    // PayPal's Transaction Search allows at most a 31-day window per request.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);
    private const int ReportingPageSize = 500;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly ILogger<PayPalGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalGateway(IHttpClientFactory httpClientFactory,
        PayPalTokenProvider tokenProvider,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Authorize (hold the money)
    // ---------------------------------------------------------------------

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        string orderReference, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = BuildCardNode(card, includeRawNumber: true, vaultId: null);
        return CreateAuthorizeOrderAsync(amount, currency, orderReference, cardNode, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string orderReference, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return CreateAuthorizeOrderAsync(amount, currency, orderReference, cardNode, idempotencyKey, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizeOrderAsync(decimal amount, string currency,
        string orderReference, Dictionary<string, object?> cardNode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = orderReference,
                    ["amount"] = new Dictionary<string, object?>
                    {
                        ["currency_code"] = currency,
                        ["value"] = FormatAmount(amount)
                    }
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = cardNode
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey, representation: true, cancellationToken, "Authorize order");

        return ParseAuthorizationFromOrder(doc.RootElement);
    }

    private PayPalAuthorizationResult ParseAuthorizationFromOrder(JsonElement root)
    {
        var orderId = GetString(root, "id") ?? string.Empty;
        var orderStatus = GetString(root, "status") ?? string.Empty;

        if (TryGetFirst(root, "purchase_units", out var pu) &&
            pu.TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("authorizations", out var auths) &&
            auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
        {
            var auth = auths[0];
            return new PayPalAuthorizationResult(
                PayPalOrderId: orderId,
                AuthorizationId: GetString(auth, "id") ?? string.Empty,
                AuthorizationStatus: GetString(auth, "status") ?? orderStatus,
                ExpiresAt: GetDate(auth, "expiration_time"),
                OrderStatus: orderStatus,
                RequiresPayerAction: false);
        }

        // No authorization was produced. If PayPal is asking the shopper to approve the payment
        // in a browser (3-D Secure), this integration must stop rather than build an approval loop.
        if (orderStatus.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            HasLink(root, "payer-action"))
        {
            throw new PayPalException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This integration does not perform a browser approval round-trip.")
            {
                RequiresPayerAction = true,
                IssueName = "PAYER_ACTION_REQUIRED"
            };
        }

        throw new PayPalException(
            $"PayPal order {orderId} returned status '{orderStatus}' with no authorization to act on.");
    }

    // ---------------------------------------------------------------------
    // Authorization lifecycle: read / reauthorize / capture / void
    // ---------------------------------------------------------------------

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, idempotencyKey: null, representation: false, cancellationToken, "Get authorization");

        var root = doc.RootElement;
        return new PayPalAuthorizationResult(
            PayPalOrderId: string.Empty,
            AuthorizationId: GetString(root, "id") ?? authorizationId,
            AuthorizationStatus: GetString(root, "status") ?? string.Empty,
            ExpiresAt: GetDate(root, "expiration_time"),
            OrderStatus: string.Empty,
            RequiresPayerAction: false);
    }

    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = FormatAmount(amount)
            }
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            idempotencyKey, representation: true, cancellationToken, "Reauthorize");

        var root = doc.RootElement;
        return new PayPalReauthorizationResult(
            AuthorizationId: GetString(root, "id") ?? authorizationId,
            AuthorizationStatus: GetString(root, "status") ?? string.Empty,
            ExpiresAt: GetDate(root, "expiration_time"));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = FormatAmount(amount)
            },
            ["final_capture"] = true,
            ["invoice_id"] = orderReference
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            idempotencyKey, representation: true, cancellationToken, "Capture");

        var root = doc.RootElement;
        var gross = amount;
        var fee = 0m;
        var net = amount;
        var currencyCode = currency;

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = GetMoney(breakdown, "gross_amount", out currencyCode) ?? gross;
            fee = GetMoney(breakdown, "paypal_fee", out _) ?? 0m;
            net = GetMoney(breakdown, "net_amount", out _) ?? (gross - fee);
        }

        return new PayPalCaptureResult(
            CaptureId: GetString(root, "id") ?? string.Empty,
            CaptureStatus: GetString(root, "status") ?? string.Empty,
            GrossAmount: gross,
            PayPalFee: fee,
            NetAmount: net,
            CurrencyCode: currencyCode ?? currency);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null,
            idempotencyKey, representation: false, cancellationToken, "Void authorization");
    }

    // ---------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(invoiceId))
        {
            body["invoice_id"] = invoiceId;
        }
        if (amount.HasValue)
        {
            body["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = FormatAmount(amount.Value)
            };
        }

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey, representation: true, cancellationToken, "Refund");

        var root = doc.RootElement;
        var refundedAmount = GetMoney(root, "amount", out var currencyCode) ?? amount ?? 0m;

        return new PayPalRefundResult(
            RefundId: GetString(root, "id") ?? string.Empty,
            RefundStatus: GetString(root, "status") ?? string.Empty,
            Amount: refundedAmount,
            CurrencyCode: currencyCode ?? currency);
    }

    // ---------------------------------------------------------------------
    // Vault (saved cards)
    // ---------------------------------------------------------------------

    public async Task<PayPalVaultResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardNode(card, includeRawNumber: true, vaultId: null)
            }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            idempotencyKey, representation: false, cancellationToken, "Vault card");

        var root = doc.RootElement;
        var vaultId = GetString(root, "id")
            ?? throw new PayPalException("PayPal vault response did not contain a token id.");

        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            returnedCustomerId = GetString(customer, "id");
        }

        string brand = "CARD";
        string last4 = "----";
        string? expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry");
        }

        return new PayPalVaultResult(vaultId, returnedCustomerId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null,
            idempotencyKey: null, representation: false, cancellationToken, "Delete vaulted card");
    }

    // ---------------------------------------------------------------------
    // Reconciliation (Transaction Search)
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PayPalException("Reconciliation 'to' must not be earlier than 'from'.");
        }

        var results = new List<PayPalTransaction>();

        // Cover the whole range by walking it in <=31-day windows, and page through every
        // page of each window so results are not limited to the first page.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        int page = 1;
        int totalPages;
        do
        {
            var url = "/v1/reporting/transactions" +
                      $"?start_date={Uri.EscapeDataString(FormatReportingDate(start))}" +
                      $"&end_date={Uri.EscapeDataString(FormatReportingDate(end))}" +
                      "&fields=transaction_info" +
                      $"&page_size={ReportingPageSize}" +
                      $"&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, url, null, idempotencyKey: null,
                representation: false, cancellationToken, "Transaction search");
            var root = doc.RootElement;

            if (root.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (d.TryGetProperty("transaction_info", out var info))
                    {
                        sink.Add(ParseTransaction(info));
                    }
                }
            }

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                ? tp.GetInt32()
                : 1;
            page++;
        }
        while (page <= totalPages);
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var amount = GetMoney(info, "transaction_amount", out var currency) ?? 0m;
        var fee = GetMoney(info, "fee_amount", out _) ?? 0m;
        return new PayPalTransaction(
            TransactionId: GetString(info, "transaction_id") ?? string.Empty,
            TransactionStatus: GetString(info, "transaction_status"),
            EventCode: GetString(info, "transaction_event_code"),
            Amount: amount,
            FeeAmount: fee,
            CurrencyCode: currency,
            InitiationDate: GetDate(info, "transaction_initiation_date"),
            InvoiceId: GetString(info, "invoice_id"),
            CustomField: GetString(info, "custom_field"));
    }

    // ---------------------------------------------------------------------
    // HTTP plumbing
    // ---------------------------------------------------------------------

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, bool representation, CancellationToken cancellationToken, string operation)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path);

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (representation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ThrowPayPalError(operation, response.StatusCode, content);
        }

        return string.IsNullOrWhiteSpace(content) ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
    }

    private void ThrowPayPalError(string operation, HttpStatusCode status, string content)
    {
        string? name = null;
        string? message = null;
        string? issue = null;
        string? debugId = null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                issue = GetString(details[0], "issue");
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with status only.
        }

        var requiresPayerAction = string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        var summary = $"{operation} failed (HTTP {(int)status}): " +
                      $"{name ?? "error"}{(issue is null ? "" : $"/{issue}")}" +
                      $"{(message is null ? "" : $" - {message}")}" +
                      $"{(debugId is null ? "" : $" [debug_id={debugId}]")}";

        _logger.LogWarning("PayPal {Operation} failed with status {Status}, issue {Issue}.",
            operation, (int)status, issue ?? name);

        throw new PayPalException(summary)
        {
            IssueName = issue ?? name,
            RequiresPayerAction = requiresPayerAction
        };
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Dictionary<string, object?> BuildCardNode(CardDetails card, bool includeRawNumber, string? vaultId)
    {
        var node = new Dictionary<string, object?>();
        if (vaultId is not null)
        {
            node["vault_id"] = vaultId;
            return node;
        }

        if (includeRawNumber)
        {
            node["number"] = card.Number;
            node["expiry"] = card.Expiry;
            if (!string.IsNullOrEmpty(card.SecurityCode)) node["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrEmpty(card.CardholderName)) node["name"] = card.CardholderName;

        var billing = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(card.BillingAddressLine1)) billing["address_line_1"] = card.BillingAddressLine1;
        if (!string.IsNullOrEmpty(card.BillingCity)) billing["admin_area_2"] = card.BillingCity;
        if (!string.IsNullOrEmpty(card.BillingState)) billing["admin_area_1"] = card.BillingState;
        if (!string.IsNullOrEmpty(card.BillingPostalCode)) billing["postal_code"] = card.BillingPostalCode;
        if (!string.IsNullOrEmpty(card.BillingCountryCode)) billing["country_code"] = card.BillingCountryCode;
        if (billing.Count > 0) node["billing_address"] = billing;

        // Do not force SCA; let PayPal decide, and stop if a browser challenge is required.
        node["attributes"] = new Dictionary<string, object?>
        {
            ["verification"] = new Dictionary<string, object?> { ["method"] = "SCA_WHEN_REQUIRED" }
        };

        return node;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement el, string prop)
    {
        var s = GetString(el, prop);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;
    }

    private static decimal? GetMoney(JsonElement parent, string prop, out string? currencyCode)
    {
        currencyCode = null;
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(prop, out var money) &&
            money.ValueKind == JsonValueKind.Object)
        {
            currencyCode = GetString(money, "currency_code");
            var value = GetString(money, "value");
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }
        }
        return null;
    }

    private static bool TryGetFirst(JsonElement root, string arrayProp, out JsonElement first)
    {
        first = default;
        if (root.TryGetProperty(arrayProp, out var arr) &&
            arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
        {
            first = arr[0];
            return true;
        }
        return false;
    }

    private static bool HasLink(JsonElement root, string rel)
    {
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), rel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
