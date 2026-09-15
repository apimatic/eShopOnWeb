using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string reference, decimal amount, CardRequest? card,
        string? vaultId, int attempt, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string reference, string authorizationId, decimal amount,
        CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string reference, string authorizationId, decimal amount,
        CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string reference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string requestId, string captureId, decimal amount, string reference,
        string? note, CancellationToken cancellationToken);
    Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalSavedCardResult> SaveCardAsync(string customerId, CardRequest card, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PayPalAuthorizationResult(string PayPalOrderId, string PayPalOrderStatus,
    string AuthorizationId, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal Amount, string Currency,
    decimal? PayPalFee, decimal? NetAmount, DateTimeOffset CreatedAt);
public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);
public sealed record PayPalSavedCardResult(string VaultId, string Brand, string Last4, string Expiry);
public sealed record PayPalTransactionResult(string TransactionId, string? ReferenceId, string? EventCode, string? InvoiceId,
    DateTimeOffset? TransactionTime, decimal? Amount, string? Currency, decimal? Fee, string? Status);

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private bool _configured;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    public string Currency => _options.Currency.ToUpperInvariant();

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string reference, decimal amount,
        CardRequest? card, string? vaultId, int attempt, CancellationToken cancellationToken)
    {
        object cardSource = vaultId is not null
            ? new
            {
                vault_id = vaultId,
                stored_credential = new { payment_initiator = "CUSTOMER", payment_type = "ONE_TIME", usage = "SUBSEQUENT" }
            }
            : CardPayload(card ?? throw new ArgumentNullException(nameof(card)));

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = reference,
                    custom_id = reference,
                    invoice_id = reference,
                    amount = Money(amount)
                }
            },
            payment_source = new { card = cardSource }
        };

        using var json = await SendRequiredAsync(HttpMethod.Post, "v2/checkout/orders", body,
            $"{reference}-pay-{attempt}", cancellationToken);
        EnsureNoBrowserChallenge(json.RootElement);
        return ParseOrderAuthorization(json.RootElement);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendRequiredAsync(HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty, string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string reference, string authorizationId,
        decimal amount, CancellationToken cancellationToken)
    {
        using var json = await SendRequiredAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount) }, $"{reference}-reauthorize", cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty, string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string reference, string authorizationId, decimal amount,
        CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount), invoice_id = reference, final_capture = true };
        using var json = await SendRequiredAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, $"{reference}-capture", cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var json = await SendRequiredAsync(HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<string> VoidAsync(string reference, string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, $"{reference}-void", cancellationToken, allowNoContent: true);
        return json is null ? "VOIDED" : OptionalString(json.RootElement, "status") ?? "VOIDED";
    }

    public async Task<PayPalRefundResult> RefundAsync(string requestId, string captureId, decimal amount,
        string reference, string? note, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount), custom_id = reference, invoice_id = reference, note_to_payer = note };
        using var json = await SendRequiredAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body, requestId, cancellationToken);
        return ParseRefund(json.RootElement);
    }

    public async Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        using var json = await SendRequiredAsync(HttpMethod.Get,
            $"v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);
        return ParseRefund(json.RootElement);
    }

    private static PayPalRefundResult ParseRefund(JsonElement root)
    {
        var money = RequiredProperty(root, "amount");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseMoney(money), RequiredString(money, "currency_code"), OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(string customerId, CardRequest card,
        CancellationToken cancellationToken)
    {
        var operationId = $"eshop-vault-{Guid.NewGuid():N}";
        var setupBody = new
        {
            customer = new { merchant_customer_id = customerId },
            payment_source = new { card = CardPayload(card) }
        };
        using var setup = await SendRequiredAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            $"{operationId}-setup", cancellationToken);
        EnsureNoBrowserChallenge(setup.RootElement);
        var setupId = RequiredString(setup.RootElement, "id");

        var tokenBody = new
        {
            payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } },
            customer = new { merchant_customer_id = customerId }
        };
        using var token = await SendRequiredAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody,
            $"{operationId}-token", cancellationToken);
        var source = RequiredProperty(RequiredProperty(token.RootElement, "payment_source"), "card");
        return new PayPalSavedCardResult(RequiredString(token.RootElement, "id"),
            RequiredString(source, "brand"), RequiredString(source, "last_digits"), RequiredString(source, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
                cancellationToken, allowNoContent: true);
        }
        catch (PayPalException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // Deleting an already-absent remote token is idempotent in effect.
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<PayPalTransactionResult>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;
            var page = 1;
            while (true)
            {
                var path = "v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page}";
                using var json = await SendRequiredAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = json.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = RequiredProperty(detail, "transaction_info");
                        var amount = OptionalMoney(info, "transaction_amount");
                        var fee = OptionalMoney(info, "fee_amount");
                        all.Add(new PayPalTransactionResult(RequiredString(info, "transaction_id"),
                            OptionalString(info, "paypal_reference_id"), OptionalString(info, "transaction_event_code"),
                            OptionalString(info, "invoice_id"),
                            OptionalDate(info, "transaction_initiation_date"), amount?.Amount,
                            amount?.Currency, fee?.Amount, OptionalString(info, "transaction_status")));
                    }
                }
                var totalPages = OptionalInt(root, "total_pages") ?? page;
                if (page >= totalPages) break;
                page++;
            }
            windowStart = windowEnd;
        }
        return all.GroupBy(t => new { t.TransactionId, t.ReferenceId, t.EventCode, t.InvoiceId,
            t.TransactionTime, t.Amount, t.Currency, t.Fee, t.Status }).Select(g => g.First()).ToList();
    }

    private async Task<JsonDocument> SendRequiredAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken) =>
        await SendAsync(method, path, body, requestId, cancellationToken) ??
        throw new InvalidOperationException("PayPal returned an empty response where a resource was required.");

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken, bool allowNoContent = false)
    {
        string? serialized = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (serialized is not null) request.Content = new StringContent(serialized, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(25, 125)), cancellationToken);
                continue;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseError(response.StatusCode, content);
            if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content))
            {
                if (allowNoContent) return null;
                throw new InvalidOperationException("PayPal returned an empty response where a resource was required.");
            }
            return JsonDocument.Parse(content);
        }
        throw new InvalidOperationException("PayPal retry policy was exhausted.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!_configured)
            {
                _options.Validate();
                _httpClient.BaseAddress = _options.ResolveBaseUri();
                _configured = true;
            }
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(25, 125)), cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw ParseError(response.StatusCode, content);
                using var json = JsonDocument.Parse(content);
                _accessToken = RequiredString(json.RootElement, "access_token");
                var expiresIn = OptionalInt(json.RootElement, "expires_in") ?? 300;
                _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                return _accessToken;
            }
            throw new InvalidOperationException("PayPal token retry policy was exhausted.");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalException ParseError(HttpStatusCode statusCode, string content)
    {
        var name = "PAYPAL_ERROR";
        var message = "PayPal could not complete the request.";
        string? issue = null;
        string? debugId = null;
        try
        {
            using var json = JsonDocument.Parse(content);
            name = OptionalString(json.RootElement, "name") ?? name;
            message = OptionalString(json.RootElement, "message") ?? message;
            debugId = OptionalString(json.RootElement, "debug_id");
            if (json.RootElement.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
                issue = OptionalString(details[0], "issue");
        }
        catch (JsonException) { }
        _logger.LogWarning("PayPal API error {StatusCode} {ErrorName} {Issue}; debug ID {DebugId}",
            (int)statusCode, name, issue, debugId);
        return new PayPalException((int)statusCode, name, message, issue, debugId);
    }

    private PayPalAuthorizationResult ParseOrderAuthorization(JsonElement root)
    {
        var orderId = RequiredString(root, "id");
        var orderStatus = RequiredString(root, "status");
        var units = RequiredProperty(root, "purchase_units");
        var firstUnit = units.EnumerateArray().FirstOrDefault();
        if (firstUnit.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("PayPal order has no purchase unit.");
        var payments = RequiredProperty(firstUnit, "payments");
        var authorizations = RequiredProperty(payments, "authorizations");
        var authorization = authorizations.EnumerateArray().FirstOrDefault();
        if (authorization.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("PayPal did not return an authorization for the card payment.");
        return ParseAuthorization(authorization, orderId, orderStatus);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root, string orderId, string orderStatus)
    {
        var amount = RequiredProperty(root, "amount");
        return new PayPalAuthorizationResult(orderId, orderStatus, RequiredString(root, "id"),
            RequiredString(root, "status"), ParseMoney(amount), RequiredString(amount, "currency_code"),
            RequiredDate(root, "create_time"), RequiredDate(root, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = RequiredProperty(root, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoney(breakdown, "paypal_fee")?.Amount;
            net = OptionalMoney(breakdown, "net_amount")?.Amount;
        }
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseMoney(amount), RequiredString(amount, "currency_code"), fee, net,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static object CardPayload(CardRequest card) => new
    {
        name = card.Name,
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private object Money(decimal amount) => new { currency_code = Currency, value = amount.ToString("F2", CultureInfo.InvariantCulture) };
    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static void EnsureNoBrowserChallenge(JsonElement root)
    {
        var status = OptionalString(root, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
                new[] { "approve", "payer-action" }.Contains(OptionalString(link, "rel"), StringComparer.OrdinalIgnoreCase))))
        {
            throw new CommerceException(409, "PayPal browser challenge required",
                "PayPal requires browser approval for this card. This API-only integration does not support an approval round-trip.");
        }
    }

    private static JsonElement RequiredProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new InvalidOperationException($"PayPal response omitted {name}.");
    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new InvalidOperationException($"PayPal response omitted {name}.");
    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static DateTimeOffset RequiredDate(JsonElement element, string name) =>
        OptionalDate(element, name) ?? throw new InvalidOperationException($"PayPal response omitted {name}.");
    private static DateTimeOffset? OptionalDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    private static decimal ParseMoney(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static (decimal Amount, string Currency)? OptionalMoney(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money)) return null;
        return (ParseMoney(money), RequiredString(money, "currency_code"));
    }
}
