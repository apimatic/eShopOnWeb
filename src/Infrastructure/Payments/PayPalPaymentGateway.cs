using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// A deliberately small client implemented against the OpenAPI documents in api-specs/paypal.
/// No PayPal SDK is used. Paths, headers and wire models here correspond to Checkout Orders v2,
/// Payments v2, Vault Payment Tokens v3 and Transaction Search v1.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string invoiceId,
        string customId, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = customId,
                    custom_id = customId,
                    invoice_id = invoiceId,
                    amount = Money(amount, currency)
                }
            }
        };
        using var document = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId,
            cancellationToken, preferRepresentation: true);
        var root = document.RootElement;
        return new PayPalOrderResult(RequiredString(root, "id"), RequiredString(root, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId,
        PayPalPaymentSource source, string requestId, CancellationToken cancellationToken)
    {
        object card = source.Card is not null
            ? CardBody(source.Card)
            : new
            {
                vault_id = source.VaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };
        var body = new { payment_source = new { card } };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", body, requestId,
            cancellationToken, preferRepresentation: true);
        return ParseOrderAuthorization(document.RootElement);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(document.RootElement, null, null, false);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken,
            preferRepresentation: true);
        return ParseAuthorization(document.RootElement, null, null, false);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null, requestId,
            cancellationToken, allowEmptyBody: true, preferRepresentation: true);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), invoice_id = invoiceId, final_capture = true };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", body, requestId,
            cancellationToken, preferRepresentation: true);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var money = root.GetProperty("amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoney(breakdown, "paypal_fee");
            net = OptionalMoney(breakdown, "net_amount");
        }
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseDecimal(money, "value"), RequiredString(money, "currency_code"), fee, net,
            OptionalDate(root, "create_time"));
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string customId, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), custom_id = customId };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body, requestId,
            cancellationToken, preferRepresentation: true);
        var root = document.RootElement;
        var money = root.GetProperty("amount");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseDecimal(money, "value"), RequiredString(money, "currency_code"),
            OptionalDate(root, "create_time"));
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCard card, string merchantCustomerId,
        string setupRequestId, string tokenRequestId, CancellationToken cancellationToken)
    {
        var setupBody = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new { card = CardBody(card) }
        };
        using var setupDocument = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            setupRequestId, cancellationToken);
        var setup = setupDocument.RootElement;
        var setupStatus = OptionalString(setup, "status");
        if (setupStatus == "PAYER_ACTION_REQUIRED")
        {
            throw new PayPalApiException(HttpStatusCode.Conflict, "PAYER_ACTION_REQUIRED",
                "The issuer requires browser approval; this API supports headless card flows only.",
                null, "PAYER_ACTION_REQUIRED");
        }
        if (setupStatus is not ("APPROVED" or "CREATED"))
        {
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity, "VAULT_SETUP_FAILED",
                $"The card setup token has status '{setupStatus ?? "UNKNOWN"}'.", null, setupStatus);
        }

        var setupId = RequiredString(setup, "id");
        var tokenBody = new
        {
            payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } }
        };
        using var tokenDocument = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            tokenRequestId, cancellationToken);
        var token = tokenDocument.RootElement;
        var cardResponse = token.GetProperty("payment_source").GetProperty("card");
        string? customerId = null;
        if (token.TryGetProperty("customer", out var customer)) customerId = OptionalString(customer, "id");
        return new PayPalVaultResult(RequiredString(token, "id"), customerId,
            RequiredString(cardResponse, "brand"), RequiredString(cardResponse, "last_digits"),
            RequiredString(cardResponse, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendJsonAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null, cancellationToken,
                allowEmptyBody: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state already exists; deletion is idempotent in effect.
        }
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        const string reportDateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        var query = $"start_date={Uri.EscapeDataString(from.UtcDateTime.ToString(reportDateFormat, CultureInfo.InvariantCulture))}" +
            $"&end_date={Uri.EscapeDataString(to.UtcDateTime.ToString(reportDateFormat, CultureInfo.InvariantCulture))}" +
            $"&fields=transaction_info&page_size={pageSize}&page={page}";
        using var document = await SendJsonAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null,
            null, cancellationToken);
        var root = document.RootElement;
        var transactions = new List<PayPalTransaction>();
        if (root.TryGetProperty("transaction_details", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                var amount = OptionalMoney(info, "transaction_amount");
                string? currency = null;
                if (info.TryGetProperty("transaction_amount", out var amountElement))
                    currency = OptionalString(amountElement, "currency_code");
                transactions.Add(new PayPalTransaction(
                    RequiredString(info, "transaction_id"), OptionalString(info, "paypal_reference_id"),
                    OptionalString(info, "paypal_reference_id_type"),
                    OptionalString(info, "transaction_event_code"),
                    OptionalDate(info, "transaction_initiation_date"),
                    OptionalDate(info, "transaction_updated_date"), amount, OptionalMoney(info, "fee_amount"),
                    currency, OptionalString(info, "transaction_status"), OptionalString(info, "invoice_id"),
                    OptionalString(info, "custom_field")));
            }
        }
        return new PayPalTransactionPage(transactions,
            root.TryGetProperty("page", out var pageElement) ? pageElement.GetInt32() : page,
            root.TryGetProperty("total_pages", out var totalPages) ? totalPages.GetInt32() : null,
            root.TryGetProperty("total_items", out var totalItems) ? totalItems.GetInt32() : null);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmptyBody = false,
        bool preferRepresentation = false)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (preferRepresentation)
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) &&
                attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw await CreateExceptionAsync(response, cancellationToken);

            if (response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent)
                return JsonDocument.Parse("{}");

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (allowEmptyBody && stream.CanSeek && stream.Length == 0) return JsonDocument.Parse("{}");
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new(_settings.ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/'));

    private static async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string name = response.StatusCode.ToString();
        string message = "The payment processor rejected the request.";
        string? debugId = null;
        string? issue = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            name = OptionalString(root, "name") ?? name;
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
            }
        }
        catch (JsonException) { }
        return new PayPalApiException(response.StatusCode, name, message, debugId, issue);
    }

    private static object CardBody(PayPalCard card) => new
    {
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        name = card.Name,
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

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseOrderAuthorization(JsonElement root)
    {
        var payerActionRequired = OptionalString(root, "status") == "PAYER_ACTION_REQUIRED" ||
            (root.TryGetProperty("links", out var links) && links.EnumerateArray()
                .Any(x => OptionalString(x, "rel") is "payer-action" or "approve"));
        string? brand = null;
        string? last4 = null;
        if (root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var card))
        {
            brand = OptionalString(card, "brand");
            last4 = OptionalString(card, "last_digits");
        }
        if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0 ||
            !units[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            if (payerActionRequired)
                return new PayPalAuthorizationResult(string.Empty, "PAYER_ACTION_REQUIRED", 0, string.Empty,
                    null, null, brand, last4, true);
            throw new JsonException("PayPal authorize response did not include an authorization.");
        }
        return ParseAuthorization(authorizations[0], brand, last4, payerActionRequired) with
        {
            OrderStatus = OptionalString(root, "status")
        };
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement authorization, string? brand,
        string? last4, bool payerActionRequired)
    {
        var money = authorization.GetProperty("amount");
        return new PayPalAuthorizationResult(RequiredString(authorization, "id"),
            RequiredString(authorization, "status"), ParseDecimal(money, "value"),
            RequiredString(money, "currency_code"), OptionalDate(authorization, "create_time"),
            OptionalDate(authorization, "expiration_time"), brand, last4, payerActionRequired);
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new JsonException($"Required PayPal field '{property}' is absent.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal ParseDecimal(JsonElement element, string property) =>
        decimal.Parse(RequiredString(element, property), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? OptionalMoney(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && money.TryGetProperty("value", out var value)
            ? decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture)
            : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value) ? value : null;
}
