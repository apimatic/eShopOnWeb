using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(IHttpClientFactory httpClientFactory, PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency,
        string externalReference, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = externalReference,
                    custom_id = externalReference,
                    invoice_id = externalReference,
                    amount = Money(amount, currency)
                }
            }
        };
        using var document = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(document.RootElement, "id"),
            RequiredString(document.RootElement, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId,
        PayPalCard? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        if ((card == null) == string.IsNullOrWhiteSpace(vaultId))
            throw new ArgumentException("Exactly one card source is required.");

        object cardSource = card != null
            ? CardPayload(card)
            : new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };

        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            new { payment_source = new { card = cardSource } }, requestId, cancellationToken);

        ThrowIfPayerActionRequired(document.RootElement);
        return ParseOrderAuthorization(document.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = document.RootElement;
        return ParseDirectAuthorization(root);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true }, requestId, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var value) ? value : default;
        return new PayPalCaptureResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            ReadMoney(root.GetProperty("amount")), RequiredString(root.GetProperty("amount"), "currency_code"),
            ReadOptionalMoney(breakdown, "paypal_fee"), ReadOptionalMoney(breakdown, "net_amount"),
            ReadDate(root, "create_time"));
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency), custom_id = requestId }, requestId, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            ReadMoney(root.GetProperty("amount")), RequiredString(root.GetProperty("amount"), "currency_code"),
            ReadDate(root, "create_time"));
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card,
        string merchantCustomerId, string setupRequestId, string tokenRequestId,
        CancellationToken cancellationToken)
    {
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new
            {
                payment_source = new { card = CardPayload(card) },
                customer = new { merchant_customer_id = merchantCustomerId }
            }, setupRequestId, cancellationToken);

        var setupRoot = setup.RootElement;
        ThrowIfPayerActionRequired(setupRoot);
        var setupStatus = RequiredString(setupRoot, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity, "VAULT_NOT_APPROVED",
                $"PayPal returned setup-token status '{setupStatus}', so the card cannot be saved.",
                null, Array.Empty<string>());

        var customerId = setupRoot.TryGetProperty("customer", out var setupCustomer) &&
                         setupCustomer.TryGetProperty("id", out var setupCustomerId)
            ? setupCustomerId.GetString()
            : null;
        object customerPayload = customerId == null
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = customerId, merchant_customer_id = merchantCustomerId };
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new { id = RequiredString(setupRoot, "id"), type = "SETUP_TOKEN" }
                },
                customer = customerPayload
            }, tokenRequestId, cancellationToken);

        var root = token.RootElement;
        var responseCard = root.GetProperty("payment_source").GetProperty("card");
        var responseCustomerId = root.TryGetProperty("customer", out var customer) &&
                                 customer.TryGetProperty("id", out var id)
            ? id.GetString()
            : customerId;
        return new PayPalSavedCardResult(
            RequiredString(root, "id"), responseCustomerId,
            RequiredString(responseCard, "brand"), RequiredString(responseCard, "last_digits"),
            RequiredString(responseCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null,
                cancellationToken, allowEmpty: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Deleting an already-absent token has the desired idempotent effect.
        }
    }

    public async Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from) throw new ArgumentException("The reconciliation end must be after its start.");
        var results = new List<PayPalTransaction>();
        var segmentStart = from.ToUniversalTime();
        var absoluteEnd = to.ToUniversalTime();

        while (segmentStart < absoluteEnd)
        {
            var segmentEnd = segmentStart.AddDays(30) < absoluteEnd
                ? segmentStart.AddDays(30)
                : absoluteEnd;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                           $"?start_date={Uri.EscapeDataString(FormatDate(segmentStart))}" +
                           $"&end_date={Uri.EscapeDataString(FormatDate(segmentEnd))}" +
                           $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var document = await SendAsync(HttpMethod.Get, path, null, null,
                    cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        var money = info.TryGetProperty("transaction_amount", out var amount)
                            ? amount
                            : default;
                        results.Add(new PayPalTransaction(
                            RequiredString(info, "transaction_id"), OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "transaction_event_code"), OptionalString(info, "transaction_status"),
                            ReadDate(info, "transaction_initiation_date"), ReadMoney(money),
                            OptionalString(money, "currency_code") ?? string.Empty,
                            ReadOptionalMoney(info, "fee_amount"), OptionalString(info, "invoice_id"),
                            OptionalString(info, "custom_field")));
                    }
                }
                totalPages = root.TryGetProperty("total_pages", out var count) ? count.GetInt32() : page;
                page++;
            } while (page <= totalPages);

            if (segmentEnd == absoluteEnd) break;
            segmentStart = segmentEnd.AddSeconds(1);
        }
        return results;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        _options.EnsureValid();
        var payload = body == null ? null : JsonSerializer.Serialize(body, JsonOptions);

        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokenProvider.GetAsync(attempt == 1, cancellationToken);
            using var request = new HttpRequestMessage(method,
                $"{_options.ResolveBaseUrl().TrimEnd('/')}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (payload != null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    if (!allowEmpty) throw new InvalidOperationException("PayPal returned an empty success response.");
                    return JsonDocument.Parse("{}");
                }
                return JsonDocument.Parse(responseText);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) &&
                attempt < 2 && (method == HttpMethod.Get || !string.IsNullOrWhiteSpace(requestId)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                continue;
            }
            throw ParseError(response.StatusCode, responseText);
        }
    }

    private PayPalApiException ParseError(HttpStatusCode statusCode, string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var name = OptionalString(root, "name") ?? "PAYPAL_ERROR";
            var message = OptionalString(root, "message") ?? "PayPal rejected the request.";
            var debugId = OptionalString(root, "debug_id");
            var issues = new List<string>();
            if (root.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = OptionalString(detail, "issue");
                    var description = OptionalString(detail, "description");
                    if (issue != null) issues.Add(description == null ? issue : $"{issue}: {description}");
                }
            }
            _logger.LogWarning("PayPal request failed with status {Status}, name {Name}, debug ID {DebugId}.",
                (int)statusCode, name, debugId);
            return new PayPalApiException(statusCode, name, message, debugId, issues,
                issues.Any(x => x.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase)));
        }
        catch (JsonException)
        {
            _logger.LogWarning("PayPal request failed with status {Status} and a non-JSON response.", (int)statusCode);
            return new PayPalApiException(statusCode, "PAYPAL_ERROR", "PayPal rejected the request.",
                null, Array.Empty<string>());
        }
    }

    private static PayPalAuthorizationResult ParseOrderAuthorization(JsonElement root)
    {
        var orderId = RequiredString(root, "id");
        var orderStatus = RequiredString(root, "status");
        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments")
            .GetProperty("authorizations")[0];
        return new PayPalAuthorizationResult(orderId, orderStatus,
            RequiredString(authorization, "id"), RequiredString(authorization, "status"),
            ReadMoney(authorization.GetProperty("amount")),
            RequiredString(authorization.GetProperty("amount"), "currency_code"),
            ReadDate(authorization, "create_time"), ReadDate(authorization, "expiration_time"));
    }

    private static PayPalAuthorizationResult ParseDirectAuthorization(JsonElement root) =>
        new(string.Empty, "APPROVED",
            RequiredString(root, "id"), RequiredString(root, "status"),
            ReadMoney(root.GetProperty("amount")), RequiredString(root.GetProperty("amount"), "currency_code"),
            ReadDate(root, "create_time"), ReadDate(root, "expiration_time"));

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        var status = OptionalString(root, "status");
        var linkRequiresAction = root.TryGetProperty("links", out var links) && links.EnumerateArray()
            .Any(x => string.Equals(OptionalString(x, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase));
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || linkRequiresAction)
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity, "PAYER_ACTION_REQUIRED",
                "PayPal requires a browser challenge for this card payment.", null,
                new[] { "PAYER_ACTION_REQUIRED" }, true);
    }

    private static object CardPayload(PayPalCard card) => new
    {
        name = card.Name,
        number = new string(card.Number.Where(char.IsDigit).ToArray()),
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

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ReadMoney(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var value) &&
        decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;

    private static decimal? ReadOptionalMoney(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var money)
            ? ReadMoney(money)
            : null;

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new InvalidOperationException($"PayPal response omitted '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string property) =>
        OptionalString(element, property) is { } value && DateTimeOffset.TryParse(value,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
