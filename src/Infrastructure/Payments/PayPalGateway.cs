using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PayPalOptions> _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalGateway(HttpClient http, IOptionsMonitor<PayPalOptions> options)
    {
        _http = http;
        _options = options;
    }

    public string Currency
    {
        get
        {
            var currency = _options.CurrentValue.Currency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new PaymentException("PayPal:Currency is not configured.", 500);
            }

            return currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string invoiceId, string customId, decimal amount, PayPalCardDetails card, string requestId,
        CancellationToken cancellationToken = default) =>
        CreateAuthorizedOrderAsync(invoiceId, customId, amount, CardPayload(card), requestId, cancellationToken);

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId, string customId, decimal amount, string vaultId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var card = new Dictionary<string, object?>
        {
            ["vault_id"] = vaultId,
            ["stored_credential"] = new Dictionary<string, string>
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "UNSCHEDULED",
                ["usage"] = "SUBSEQUENT"
            }
        };
        return CreateAuthorizedOrderAsync(invoiceId, customId, amount, card, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return ReadAuthorization(doc.RootElement, paypalOrderId: string.Empty, paypalOrderStatus: GetString(doc.RootElement, "status") ?? string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = Currency, value = MoneyFormatter.ToPayPalValue(amount, Currency) }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken, true);
        return ReadAuthorization(doc.RootElement, paypalOrderId: string.Empty, paypalOrderStatus: GetString(doc.RootElement, "status") ?? string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = Currency, value = MoneyFormatter.ToPayPalValue(amount, Currency) },
            final_capture = true,
            invoice_id = invoiceId
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken, true);
        return ReadCapture(doc.RootElement);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", new { }, requestId, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal amount, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = Currency, value = MoneyFormatter.ToPayPalValue(amount, Currency) }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken, true);
        var root = doc.RootElement;
        return new PayPalRefundResult(
            GetRequiredString(root, "id"),
            GetString(root, "status") ?? string.Empty,
            MoneyFormatter.Parse(GetAmountValue(root)),
            GetAmountCurrency(root) ?? Currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId, PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?> { ["card"] = CardPayload(card) };
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, string> { ["merchant_customer_id"] = merchantCustomerId },
            ["payment_source"] = paymentSource
        };

        JsonDocument doc;
        try
        {
            doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        }
        catch (PaymentException) when (requestId.Length <= 100)
        {
            var setupBody = new Dictionary<string, object?> { ["payment_source"] = paymentSource };
            using var setupDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, requestId + "-s", cancellationToken);
            EnsureNoPayerAction(setupDoc.RootElement);
            var setupId = GetRequiredString(setupDoc.RootElement, "id");
            var tokenBody = new Dictionary<string, object?>
            {
                ["customer"] = new Dictionary<string, string> { ["merchant_customer_id"] = merchantCustomerId },
                ["payment_source"] = new Dictionary<string, object?>
                {
                    ["token"] = new Dictionary<string, string> { ["id"] = setupId, ["type"] = "SETUP_TOKEN" }
                }
            };
            doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, requestId + "-t", cancellationToken);
        }

        using (doc)
        {
            EnsureNoPayerAction(doc.RootElement);
            var cardEl = GetPath(doc.RootElement, "payment_source", "card");
            return new PayPalVaultedCard(
                GetRequiredString(doc.RootElement, "id"),
                GetString(GetPath(doc.RootElement, "customer"), "id"),
                GetString(cardEl, "brand") ?? "CARD",
                GetString(cardEl, "last_digits") ?? string.Empty,
                card.Expiry,
                card.Name);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        var records = new List<PayPalTransactionRecord>();
        foreach (var (windowStart, windowEnd) in SplitDateWindows(start, end))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                var query =
                    $"start_date={Uri.EscapeDataString(FormatPayPalDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatPayPalDate(windowEnd))}" +
                    $"&fields=all&page_size=500&page={page}";
                using var doc = await SendJsonAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null, null, cancellationToken);
                var root = doc.RootElement;
                if (root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages))
                {
                    totalPages = Math.Max(pages, 1);
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = GetPath(detail, "transaction_info");
                        if (info.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var id = GetString(info, "transaction_id");
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        records.Add(new PayPalTransactionRecord(
                            id,
                            GetString(info, "paypal_reference_id"),
                            GetString(info, "invoice_id"),
                            GetString(info, "custom_field"),
                            GetString(info, "transaction_event_code"),
                            GetString(info, "transaction_status"),
                            MoneyFormatter.Parse(GetAmountValue(info, "transaction_amount")),
                            string.IsNullOrWhiteSpace(GetAmountValue(info, "fee_amount"))
                                ? null
                                : MoneyFormatter.Parse(GetAmountValue(info, "fee_amount")),
                            GetAmountCurrency(info, "transaction_amount"),
                            GetDate(info, "transaction_initiation_date")));
                    }
                }

                page++;
            } while (page <= totalPages);
        }

        return records;
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizedOrderAsync(
        string invoiceId, string customId, decimal amount, Dictionary<string, object?> card, string requestId,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId,
                    ["amount"] = new Dictionary<string, string>
                    {
                        ["currency_code"] = Currency,
                        ["value"] = MoneyFormatter.ToPayPalValue(amount, Currency)
                    }
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };

        using var created = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken, true);
        EnsureNoPayerAction(created.RootElement);
        var orderId = GetRequiredString(created.RootElement, "id");
        var authorization = FindAuthorization(created.RootElement);
        var status = GetString(created.RootElement, "status");

        if (authorization is null && !string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            using var authorized = await SendJsonAsync(
                HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize", new { }, requestId + "-a", cancellationToken, true);
            EnsureNoPayerAction(authorized.RootElement);
            authorization = FindAuthorization(authorized.RootElement);
            status = GetString(authorized.RootElement, "status");
            if (authorization is null)
            {
                throw new PaymentException("PayPal did not return an authorization for this card payment.", 502);
            }

            return ReadAuthorization(authorization.Value, orderId, status ?? string.Empty);
        }

        if (authorization is null)
        {
            throw new PaymentException("PayPal did not return an authorization for this card payment.", 502);
        }

        return ReadAuthorization(authorization.Value, orderId, status ?? string.Empty);
    }

    private static Dictionary<string, object?> CardPayload(PayPalCardDetails card)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };
        if (card.BillingAddress is not null)
        {
            payload["billing_address"] = new Dictionary<string, string?>
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = card.BillingAddress.CountryCode
            };
        }

        return payload;
    }

    private PayPalAuthorizationResult ReadAuthorization(JsonElement element, string paypalOrderId, string paypalOrderStatus)
    {
        return new PayPalAuthorizationResult(
            paypalOrderId,
            paypalOrderStatus,
            GetRequiredString(element, "id"),
            GetString(element, "status") ?? string.Empty,
            MoneyFormatter.Parse(GetAmountValue(element)),
            GetAmountCurrency(element) ?? Currency,
            GetDate(element, "expiration_time"),
            GetDate(element, "create_time"));
    }

    private PayPalCaptureResult ReadCapture(JsonElement element)
    {
        var breakdown = GetPath(element, "seller_receivable_breakdown");
        return new PayPalCaptureResult(
            GetRequiredString(element, "id"),
            GetString(element, "status") ?? string.Empty,
            MoneyFormatter.Parse(GetAmountValue(element)),
            breakdown.ValueKind == JsonValueKind.Object ? MoneyFormatter.Parse(GetAmountValue(breakdown, "paypal_fee")) : null,
            breakdown.ValueKind == JsonValueKind.Object ? MoneyFormatter.Parse(GetAmountValue(breakdown, "net_amount")) : null,
            GetAmountCurrency(element) ?? Currency);
    }

    private static JsonElement? FindAuthorization(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            var auths = GetPath(unit, "payments", "authorizations");
            if (auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(GetString(auth, "id")))
                {
                    return auth;
                }
            }
        }

        return null;
    }

    private static void EnsureNoPayerAction(JsonElement root)
    {
        var status = GetString(root, "status");
        var needsAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            needsAction |= links.EnumerateArray().Any(l =>
                string.Equals(GetString(l, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase));
        }

        if (needsAction)
        {
            throw new PayerActionRequiredException(
                "PayPal asked the shopper to complete a browser challenge (3-D Secure). This integration does not collect a browser round-trip, so the payment was stopped.");
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method, string relativeUrl, object? body, string? requestId, CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        using var response = await SendAsync(method, relativeUrl, body, requestId, cancellationToken, preferRepresentation);
        await EnsureSuccessAsync(response, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativeUrl, object? body, string? requestId, CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = CreateRequest(method, relativeUrl, body, requestId, preferRepresentation, token);
        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            token = await GetAccessTokenAsync(cancellationToken);
            request = CreateRequest(method, relativeUrl, body, requestId, preferRepresentation, token);
            response = await _http.SendAsync(request, cancellationToken);
        }

        return response;
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method, string relativeUrl, object? body, string? requestId, bool preferRepresentation, string token)
    {
        var request = new HttpRequestMessage(method, _options.CurrentValue.ResolveBaseUrl() + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (raw.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("\"3DS\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal asked the shopper to complete a browser challenge (3-D Secure). This integration does not collect a browser round-trip, so the payment was stopped.");
        }

        string message = $"PayPal request failed with {(int)response.StatusCode}.";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var paypalMessage = GetString(root, "message");
            var name = GetString(root, "name");
            var debugId = GetString(root, "debug_id");
            var details = new List<string>();
            if (root.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsEl.EnumerateArray())
                {
                    details.Add($"{GetString(detail, "issue")}: {GetString(detail, "description")}".Trim(':', ' '));
                }
            }

            message = string.Join(" ", new[] { name, paypalMessage, string.Join("; ", details) }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(debugId))
            {
                message += $" (PayPal debug id {debugId})";
            }
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                message = raw;
            }
        }

        var status = (int)response.StatusCode;
        if (status is 401 or 403)
        {
            status = 502;
        }

        throw new PaymentException(message, status >= 400 ? status : 502);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken!;
            }

            var options = _options.CurrentValue;
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new PaymentException("PayPal:ClientId and PayPal:ClientSecret must be configured.", 500);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, options.ResolveBaseUrl() + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _http.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentException($"PayPal token request failed: {(int)response.StatusCode}.", 502);
            }

            using var doc = JsonDocument.Parse(raw);
            _accessToken = GetRequiredString(doc.RootElement, "access_token");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds
                : 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    private static string FormatPayPalDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitDateWindows(DateTimeOffset start, DateTimeOffset end)
    {
        var cursor = start;
        while (cursor < end)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }
    }

    private static JsonElement GetPath(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return default;
            }
        }

        return current;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string GetRequiredString(JsonElement element, string name) =>
        GetString(element, name) ?? throw new PaymentException($"PayPal response did not include '{name}'.", 502);

    private static string? GetAmountValue(JsonElement element, string propertyName = "amount")
    {
        var amount = propertyName == "amount" ? GetPath(element, "amount") : GetPath(element, propertyName);
        return GetString(amount, "value");
    }

    private static string? GetAmountCurrency(JsonElement element, string propertyName = "amount")
    {
        var amount = propertyName == "amount" ? GetPath(element, "amount") : GetPath(element, propertyName);
        return GetString(amount, "currency_code");
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
