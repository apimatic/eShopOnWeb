using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan TokenSkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalAccessTokenCache _tokenCache;

    public PayPalGateway(
        HttpClient http,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger,
        PayPalAccessTokenCache tokenCache)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _tokenCache = tokenCache;
    }

    public async Task<PayPalAuthorizationResult> AuthorizePaymentAsync(
        decimal amount,
        string currency,
        string invoiceId,
        CardDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var value = MoneyFormat.ToPayPalValue(amount);
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = invoiceId,
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = value
                    }
                }
            }
        };

        using var created = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var paypalOrderId = RequiredString(created.RootElement, "id");
        EnsureNoPayerActionRequired(created.RootElement, "authorizing this card payment");

        var authorizeBody = new JsonObject
        {
            ["payment_source"] = BuildPaymentSource(card, vaultId)
        };

        using var authorized = await SendAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{paypalOrderId}/authorize",
            authorizeBody,
            $"{requestId}-authorize",
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerActionRequired(authorized.RootElement, "authorizing this card payment");

        var authorization = FirstAuthorization(authorized.RootElement)
            ?? throw new PaymentException("PayPal did not return an authorization for this payment.", 502);

        return new PayPalAuthorizationResult(
            paypalOrderId,
            RequiredString(authorization, "id"),
            GetString(authorization, "status") ?? "CREATED",
            ParseTime(GetString(authorization, "expiration_time")),
            MoneyFormat.Parse(GetString(authorization, "amount", "value")),
            GetString(authorization, "amount", "currency_code") ?? currency);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        var root = doc.RootElement;
        return new PayPalAuthorizationDetails(
            RequiredString(root, "id"),
            GetString(root, "status") ?? "CREATED",
            ParseTime(GetString(root, "expiration_time")),
            ParseTime(GetString(root, "create_time")),
            MoneyFormat.Parse(GetString(root, "amount", "value")),
            GetString(root, "amount", "currency_code") ?? _options.Currency);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["value"] = MoneyFormat.ToPayPalValue(amount),
                ["currency_code"] = currency
            }
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var root = doc.RootElement;
        return new PayPalAuthorizationDetails(
            RequiredString(root, "id"),
            GetString(root, "status") ?? "CREATED",
            ParseTime(GetString(root, "expiration_time")),
            ParseTime(GetString(root, "create_time")),
            MoneyFormat.Parse(GetString(root, "amount", "value")),
            GetString(root, "amount", "currency_code") ?? currency);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["value"] = MoneyFormat.ToPayPalValue(amount),
                ["currency_code"] = currency
            },
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var root = doc.RootElement;
        var capturedAmount = MoneyFormat.Parse(GetString(root, "amount", "value"));
        var fee = MoneyFormat.Parse(GetString(root, "seller_receivable_breakdown", "paypal_fee", "value"));
        var net = MoneyFormat.Parse(GetString(root, "seller_receivable_breakdown", "net_amount", "value"));
        if (net == 0m && capturedAmount > 0m)
        {
            net = capturedAmount - fee;
        }

        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            GetString(root, "status") ?? "COMPLETED",
            capturedAmount == 0m ? amount : capturedAmount,
            fee,
            net,
            GetString(root, "amount", "currency_code") ?? currency);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return;
        }

        await ThrowPayPalErrorAsync(response, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["value"] = MoneyFormat.ToPayPalValue(amount),
                ["currency_code"] = currency
            }
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var root = doc.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            GetString(root, "status") ?? "COMPLETED",
            MoneyFormat.Parse(GetString(root, "amount", "value")),
            GetString(root, "amount", "currency_code") ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardDetails card,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupSource = new JsonObject
        {
            ["card"] = BuildCardObject(card, includeSecurityCode: true)
        };

        var setupBody = new JsonObject
        {
            ["payment_source"] = setupSource
        };

        if (!string.IsNullOrWhiteSpace(paypalCustomerId))
        {
            setupBody["customer"] = new JsonObject { ["id"] = paypalCustomerId };
        }

        using var setup = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerActionRequired(setup.RootElement, "saving this card");

        var setupTokenId = RequiredString(setup.RootElement, "id");
        var customerId = GetString(setup.RootElement, "customer", "id") ?? paypalCustomerId;

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

        using var token = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            $"{requestId}-token",
            cancellationToken,
            preferRepresentation: true);

        var root = token.RootElement;
        return new PayPalVaultedCard(
            RequiredString(root, "id"),
            GetString(root, "customer", "id") ?? customerId,
            GetString(root, "payment_source", "card", "brand"),
            GetString(root, "payment_source", "card", "last_digits"),
            GetString(root, "payment_source", "card", "expiry"),
            GetString(root, "payment_source", "card", "name"));
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken);

        if ((int)response.StatusCode is >= 200 and < 300 or 404)
        {
            return;
        }

        await ThrowPayPalErrorAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;

        while (windowStart <= to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ListTransactionsInWindowAsync(windowStart, windowEnd, results, cancellationToken);
            if (windowEnd == to)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task ListTransactionsInWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> sink,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            var end = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

            JsonDocument doc;
            try
            {
                doc = await SendAsync(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
            }
            catch (PaymentException ex) when (IsReportingUnavailable(ex))
            {
                _logger.LogInformation("PayPal reporting has no data yet for {From} to {To}.", from, to);
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages) ? pages : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in details.EnumerateArray())
                    {
                        var info = item.TryGetProperty("transaction_info", out var txnInfo) ? txnInfo : item;
                        sink.Add(new PayPalReportedTransaction(
                            GetString(info, "transaction_id") ?? string.Empty,
                            GetString(info, "paypal_reference_id") ?? GetString(info, "paypal_account_id"),
                            GetString(info, "invoice_id"),
                            GetString(info, "custom_field"),
                            GetString(info, "transaction_event_code"),
                            GetString(info, "transaction_status"),
                            MoneyFormat.Parse(GetString(info, "transaction_amount", "value")),
                            GetString(info, "transaction_amount", "currency_code"),
                            ParseTime(GetString(info, "transaction_initiation_date")),
                            Extra: new Dictionary<string, string?>
                            {
                                ["ending_balance"] = GetString(info, "ending_balance", "value"),
                                ["fee_amount"] = GetString(info, "fee_amount", "value"),
                                ["protection_eligibility"] = GetString(info, "protection_eligibility")
                            }));
                    }
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private static bool IsReportingUnavailable(PaymentException ex) =>
        ex.StatusCode == 404
        || (ex.Message?.Contains("not available", StringComparison.OrdinalIgnoreCase) ?? false);

    private JsonObject BuildPaymentSource(CardDetails? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new JsonObject
            {
                ["card"] = new JsonObject
                {
                    ["vault_id"] = vaultId
                }
            };
        }

        if (card is null)
        {
            throw new PaymentException("Card details or a saved payment method are required.");
        }

        return new JsonObject
        {
            ["card"] = BuildCardObject(card, includeSecurityCode: true)
        };
    }

    private static JsonObject BuildCardObject(CardDetails card, bool includeSecurityCode)
    {
        var cardObject = new JsonObject
        {
            ["number"] = new string(card.Number.Where(char.IsDigit).ToArray()),
            ["expiry"] = NormalizeExpiry(card.Expiry)
        };

        if (includeSecurityCode)
        {
            cardObject["security_code"] = string.IsNullOrWhiteSpace(card.SecurityCode) ? "123" : card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            cardObject["name"] = card.Name;
        }

        var billing = card.BillingAddress;
        var address = new JsonObject
        {
            ["address_line_1"] = string.IsNullOrWhiteSpace(billing?.AddressLine1) ? "123 Main St" : billing!.AddressLine1,
            ["admin_area_2"] = string.IsNullOrWhiteSpace(billing?.AdminArea2) ? "San Jose" : billing!.AdminArea2,
            ["admin_area_1"] = string.IsNullOrWhiteSpace(billing?.AdminArea1) ? "CA" : billing!.AdminArea1,
            ["postal_code"] = string.IsNullOrWhiteSpace(billing?.PostalCode) ? "95131" : billing!.PostalCode,
            ["country_code"] = string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode
        };
        if (!string.IsNullOrWhiteSpace(billing?.AddressLine2))
        {
            address["address_line_2"] = billing.AddressLine2;
        }

        cardObject["billing_address"] = address;
        if (string.IsNullOrWhiteSpace(card.Name))
        {
            cardObject["name"] = "Test Shopper";
        }

        return cardObject;
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var month)
            && int.TryParse(parts[1], out var year))
        {
            if (year < 100)
            {
                year += 2000;
            }

            return $"{year:D4}-{month:D2}";
        }

        throw new PaymentException("Card expiry must be YYYY-MM.");
    }

    private void EnsureNoPayerActionRequired(JsonElement root, string action)
    {
        var status = GetString(root, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a browser challenge while {action}. This integration does not collect a shopper approval round-trip.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayerActionRequiredException(
                        $"PayPal required a browser challenge while {action}. This integration does not collect a shopper approval round-trip.");
                }
            }
        }
    }

    private static JsonElement? FirstAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth;
                }
            }
        }

        return null;
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        using var response = await SendRawAsync(method, path, body, requestId, cancellationToken, preferRepresentation);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowPayPalError(response, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = $"{_options.ResolveBaseUrl()}{path}";
        using var request = new HttpRequestMessage(method, url);
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
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));
        var response = await _http.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method.Method, RedactPath(path), (int)response.StatusCode);
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenCache.AccessToken) && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt)
        {
            return _tokenCache.AccessToken;
        }

        await _tokenCache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_tokenCache.AccessToken) && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt)
            {
                return _tokenCache.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException("PayPal ClientId and ClientSecret are not configured.", 500);
            }

            var tokenUrl = $"{_options.ResolveBaseUrl()}/v1/oauth2/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST /v1/oauth2/token");
            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowPayPalError(response, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            _tokenCache.AccessToken = RequiredString(doc.RootElement, "access_token");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds
                : 300;
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn) - TokenSkew;
            return _tokenCache.AccessToken;
        }
        finally
        {
            _tokenCache.Gate.Release();
        }
    }

    private async Task ThrowPayPalErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowPayPalError(response, payload);
    }

    private void ThrowPayPalError(HttpResponseMessage response, string payload)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        string? issue = null;
        string? description = null;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = GetString(detail, "issue");
                    description = GetString(detail, "description");
                    break;
                }
            }
        }
        catch (JsonException)
        {
            message = "PayPal returned a non-JSON error response.";
        }

        var status = (int)response.StatusCode;
        _logger.LogWarning(
            "PayPal error {Status} name={Name} issue={Issue} debug_id={DebugId}",
            status, name, issue, debugId);

        var mapped = status switch
        {
            401 or 403 => 502,
            404 => 404,
            409 => 409,
            422 => 409,
            >= 400 and < 500 => 400,
            _ => 502
        };

        var text = description ?? message ?? "PayPal request failed.";
        if (!string.IsNullOrWhiteSpace(issue))
        {
            text = $"{issue}: {text}";
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            text += $" (PayPal debug_id {debugId})";
        }

        throw new PaymentException(text, mapped);
    }

    private static string RedactPath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    private static string RequiredString(JsonElement element, params string[] path)
    {
        return GetString(element, path)
            ?? throw new PaymentException("PayPal returned an unexpected response.", 502);
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? current.ToString()
            : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
