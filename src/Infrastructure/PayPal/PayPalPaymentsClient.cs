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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly PayPalAccessTokenCache _tokenCache;
    private readonly ILogger<PayPalPaymentsClient> _logger;

    public PayPalPaymentsClient(
        HttpClient httpClient,
        PayPalOptions options,
        PayPalAccessTokenCache tokenCache,
        ILogger<PayPalPaymentsClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _tokenCache = tokenCache;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        string invoiceId,
        string customId,
        PayPalMoney amount,
        CardAuthorizationRequest card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = BuildCardObject(card)
        };

        return AuthorizeAsync(invoiceId, customId, amount, paymentSource, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        string invoiceId,
        string customId,
        PayPalMoney amount,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = new JsonObject { ["vault_id"] = vaultId }
        };

        return AuthorizeAsync(invoiceId, customId, amount, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        return ParseAuthorizationResource(json, payPalOrderId: string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        PayPalMoney amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = MoneyObject(amount)
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        return ParseAuthorizationResource(json, payPalOrderId: string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        PayPalMoney amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = MoneyObject(amount),
            ["final_capture"] = true
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        var capture = ParseCapture(json);
        if (capture.PayPalFee == 0 && capture.NetProceeds == 0 && !string.IsNullOrEmpty(capture.CaptureId))
        {
            var detailed = await SendAsync(
                HttpMethod.Get,
                $"/v2/payments/captures/{Uri.EscapeDataString(capture.CaptureId)}",
                body: null,
                requestId: null,
                preferRepresentation: true,
                cancellationToken);
            capture = ParseCapture(detailed);
        }

        return capture;
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: new JsonObject(),
            requestId,
            preferRepresentation: true,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        PayPalMoney? amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        JsonNode body = new JsonObject();
        if (amount != null)
        {
            body = new JsonObject { ["amount"] = MoneyObject(amount) };
        }

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        var refundId = RequiredString(json, "id");
        var status = GetString(json, "status") ?? "COMPLETED";
        var refundAmount = GetDecimal(json, "amount", "value");
        var currency = GetString(json, "amount", "currency_code") ?? amount?.CurrencyCode ?? _options.Currency;

        return new PayPalRefundResult(refundId, status, refundAmount, currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardAuthorizationRequest card,
        string? existingCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCardObject(card)
            }
        };

        if (!string.IsNullOrWhiteSpace(existingCustomerId))
        {
            setupBody["customer"] = new JsonObject { ["id"] = existingCustomerId };
        }

        var setupJson = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            $"{requestId}-setup",
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerAction(setupJson);

        var setupTokenId = RequiredString(setupJson, "id");
        var setupStatus = GetString(setupJson, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw OrderPaymentException.Unprocessable(
                $"PayPal did not approve the card for vaulting (status {setupStatus}).");
        }

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

        var tokenJson = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            $"{requestId}-token",
            preferRepresentation: true,
            cancellationToken);

        var paymentTokenId = RequiredString(tokenJson, "id");
        var customerId = GetString(tokenJson, "customer", "id") ?? GetString(setupJson, "customer", "id");
        var lastDigits = GetString(tokenJson, "payment_source", "card", "last_digits")
            ?? GetString(setupJson, "payment_source", "card", "last_digits")
            ?? LastFour(card.Number);
        var brand = GetString(tokenJson, "payment_source", "card", "brand")
            ?? GetString(setupJson, "payment_source", "card", "brand");
        var expiry = GetString(tokenJson, "payment_source", "card", "expiry")
            ?? GetString(setupJson, "payment_source", "card", "expiry")
            ?? card.Expiry;
        var name = GetString(tokenJson, "payment_source", "card", "name")
            ?? GetString(setupJson, "payment_source", "card", "name")
            ?? card.Name;

        return new PayPalVaultedCard(paymentTokenId, customerId, lastDigits, brand, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
            body: null,
            requestId: null,
            preferRepresentation: false,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var chunkStart = from;
        while (chunkStart <= to)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            await AddTransactionChunkAsync(chunkStart, chunkEnd, results, cancellationToken);

            if (chunkEnd >= to)
            {
                break;
            }

            chunkStart = chunkEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task AddTransactionChunkAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"start_date={Uri.EscapeDataString(ToPayPalDate(from))}" +
                $"&end_date={Uri.EscapeDataString(ToPayPalDate(to))}" +
                "&fields=all&page_size=500&page=" + page +
                "&balance_affecting_records_only=N";

            JsonNode json;
            try
            {
                json = await SendAsync(
                    HttpMethod.Get,
                    $"/v1/reporting/transactions?{query}",
                    body: null,
                    requestId: null,
                    preferRepresentation: false,
                    cancellationToken);
            }
            catch (OrderPaymentException ex) when (
                ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "PayPal reporting returned no data for {From} to {To}: {Message}",
                    from,
                    to,
                    ex.Message);
                return;
            }

            if (json["transaction_details"] is JsonArray details)
            {
                foreach (var item in details)
                {
                    if (item is JsonObject)
                    {
                        results.Add(ParseReportedTransaction(item));
                    }
                }
            }

            totalPages = json["total_pages"]?.GetValue<int>() ?? 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        string invoiceId,
        string customId,
        PayPalMoney amount,
        JsonObject paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = "default",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId,
                    ["amount"] = MoneyObject(amount)
                }
            },
            ["payment_source"] = paymentSource
        };

        var orderJson = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerAction(orderJson);

        var payPalOrderId = RequiredString(orderJson, "id");
        var authorization = TryParseAuthorizationFromOrder(orderJson, payPalOrderId);
        if (authorization != null)
        {
            return authorization;
        }

        var authorizeJson = await SendAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            body: new JsonObject(),
            requestId: $"{requestId}-authorize",
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerAction(authorizeJson);
        authorization = TryParseAuthorizationFromOrder(authorizeJson, payPalOrderId);
        if (authorization == null)
        {
            throw OrderPaymentException.Unprocessable(
                $"PayPal created order {payPalOrderId} but did not return an authorization.");
        }

        return authorization;
    }

    private void EnsureNoPayerAction(JsonNode json)
    {
        var status = GetString(json, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(GetString(json, "id"), GetString(json, "debug_id"));
        }

        if (json["links"] is JsonArray links)
        {
            foreach (var link in links)
            {
                var rel = link?["rel"]?.GetValue<string>();
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
                {
                    var href = link?["href"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(href) &&
                        href.Contains("helios", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PayerActionRequiredException(GetString(json, "id"), GetString(json, "debug_id"));
                    }
                }
            }
        }
    }

    private PayPalAuthorizationResult? TryParseAuthorizationFromOrder(JsonNode orderJson, string payPalOrderId)
    {
        var units = orderJson["purchase_units"] as JsonArray;
        var authorizations = units?
            .OfType<JsonObject>()
            .SelectMany(unit =>
            {
                if (unit["payments"]?["authorizations"] is JsonArray array)
                {
                    return array.OfType<JsonObject>();
                }

                return Enumerable.Empty<JsonObject>();
            })
            .OfType<JsonObject>()
            .ToList();

        var first = authorizations?.FirstOrDefault();
        if (first == null)
        {
            return null;
        }

        return ParseAuthorizationResource(first, payPalOrderId);
    }

    private PayPalAuthorizationResult ParseAuthorizationResource(JsonNode json, string payPalOrderId)
    {
        var id = RequiredString(json, "id");
        var status = GetString(json, "status") ?? "CREATED";
        var currency = GetString(json, "amount", "currency_code") ?? _options.Currency;
        var value = GetDecimal(json, "amount", "value");
        var created = GetDate(json, "create_time");
        var expires = GetDate(json, "expiration_time");
        var orderId = string.IsNullOrEmpty(payPalOrderId)
            ? GetString(json, "supplementary_data", "related_ids", "order_id") ?? string.Empty
            : payPalOrderId;

        return new PayPalAuthorizationResult(
            orderId,
            id,
            status,
            new PayPalMoney(currency, value),
            created,
            expires);
    }

    private PayPalCaptureResult ParseCapture(JsonNode json)
    {
        var id = RequiredString(json, "id");
        var status = GetString(json, "status") ?? "COMPLETED";
        var captured = GetDecimal(json, "seller_receivable_breakdown", "gross_amount", "value");
        if (captured == 0)
        {
            captured = GetDecimal(json, "amount", "value");
        }

        var fee = GetDecimal(json, "seller_receivable_breakdown", "paypal_fee", "value");
        var net = GetDecimal(json, "seller_receivable_breakdown", "net_amount", "value");
        var currency = GetString(json, "seller_receivable_breakdown", "gross_amount", "currency_code")
            ?? GetString(json, "amount", "currency_code")
            ?? _options.Currency;

        return new PayPalCaptureResult(id, status, captured, fee, net, currency);
    }

    private static PayPalReportedTransaction ParseReportedTransaction(JsonNode item)
    {
        var info = item["transaction_info"] ?? item;
        var amount = GetDecimal(info, "transaction_amount", "value");
        var fee = GetNullableDecimal(info, "fee_amount", "value");
        var currency = GetString(info, "transaction_amount", "currency_code");

        return new PayPalReportedTransaction(
            GetString(info, "transaction_id") ?? string.Empty,
            GetString(info, "paypal_reference_id"),
            GetString(info, "invoice_id"),
            GetString(info, "custom_field"),
            GetString(info, "transaction_event_code"),
            GetString(info, "transaction_status"),
            GetString(info, "transaction_initiation_date") ?? GetString(info, "transaction_updated_date"),
            amount == 0 && info["transaction_amount"] == null ? null : amount,
            fee,
            currency);
    }

    private static JsonObject BuildCardObject(CardAuthorizationRequest card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            node["name"] = card.Name;
        }

        if (card.BillingAddress != null)
        {
            var address = new JsonObject();
            AddIfPresent(address, "address_line_1", card.BillingAddress.AddressLine1);
            AddIfPresent(address, "address_line_2", card.BillingAddress.AddressLine2);
            AddIfPresent(address, "admin_area_1", card.BillingAddress.AdminArea1);
            AddIfPresent(address, "admin_area_2", card.BillingAddress.AdminArea2);
            AddIfPresent(address, "postal_code", card.BillingAddress.PostalCode);
            AddIfPresent(address, "country_code", card.BillingAddress.CountryCode ?? "US");
            if (address.Count > 0)
            {
                node["billing_address"] = address;
            }
        }

        return node;
    }

    private static JsonObject MoneyObject(PayPalMoney amount) => new()
    {
        ["currency_code"] = amount.CurrencyCode,
        ["value"] = PayPalMoneyFormat.ToApiValue(amount.Value)
    };

    private static void AddIfPresent(JsonObject node, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            node[name] = value;
        }
    }

    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string relativePath,
        JsonNode? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, Combine(relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, SanitizePath(relativePath));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content))
        {
            if (response.IsSuccessStatusCode)
            {
                return allowEmpty ? new JsonObject() : throw OrderPaymentException.Unprocessable("PayPal returned an empty success response.");
            }

            throw ToGatewayException((int)response.StatusCode, content);
        }

        JsonNode json;
        try
        {
            json = JsonNode.Parse(content) ?? new JsonObject();
        }
        catch (JsonException)
        {
            _logger.LogWarning("PayPal returned non-JSON {StatusCode} for {Path}", (int)response.StatusCode, SanitizePath(relativePath));
            throw OrderPaymentException.Unprocessable($"PayPal returned HTTP {(int)response.StatusCode} with an unexpected payload.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string? debugId = GetString(json, "debug_id");
            if (string.IsNullOrEmpty(debugId) &&
                response.Headers.TryGetValues("Paypal-Debug-Id", out var values))
            {
                debugId = values.FirstOrDefault();
            }
            var name = GetString(json, "name");
            var message = GetString(json, "message");
            var issues = ReadIssues(json);
            _logger.LogWarning(
                "PayPal {Method} {Path} failed with {StatusCode} name {Name} debug {DebugId} issues {Issues} payload {Payload}",
                method.Method,
                SanitizePath(relativePath),
                (int)response.StatusCode,
                name,
                debugId,
                string.Join(", ", issues),
                body == null ? "" : RedactSecrets(body.ToJsonString()));

            if (issues.Any(i => i.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException(GetString(json, "id"), debugId);
            }

            throw ToGatewayException((int)response.StatusCode, content, name, message, debugId, issues);
        }

        return json;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw OrderPaymentException.Unprocessable("PayPal client credentials are not configured.");
        }

        var cacheKey = $"{_options.ResolveBaseUrl()}|{_options.ClientId}";
        return await _tokenCache.GetOrCreateAsync(cacheKey, async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Combine("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("Requesting PayPal OAuth token");
            using var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException((int)response.StatusCode, content);
            }

            using var document = JsonDocument.Parse(content);
            var token = document.RootElement.GetProperty("access_token").GetString()
                ?? throw OrderPaymentException.Unprocessable("PayPal OAuth response did not include access_token.");
            var expires = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 300;
            return (token, expires);
        }, cancellationToken);
    }

    private string Combine(string relativePath)
    {
        var baseUrl = _options.ResolveBaseUrl();
        if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath;
        }

        return $"{baseUrl}{relativePath}";
    }

    private static string SanitizePath(string relativePath)
    {
        var q = relativePath.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? relativePath : relativePath[..q];
    }

    private static OrderPaymentException ToGatewayException(
        int statusCode,
        string content,
        string? name = null,
        string? message = null,
        string? debugId = null,
        IReadOnlyList<string>? issues = null)
    {
        issues ??= Array.Empty<string>();
        var mapped = statusCode is 400 or 401 or 403 or 404 or 409 or 422 ? statusCode : 502;
        var detail = string.IsNullOrWhiteSpace(message) ? Truncate(content) : message;
        var issueText = issues.Count == 0 ? string.Empty : $" Issues: {string.Join(", ", issues)}.";
        var debug = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" Debug id: {debugId}.";
        var nameText = string.IsNullOrWhiteSpace(name) ? "PayPal request failed" : name;
        return new OrderPaymentException(mapped, $"{nameText}: {detail}.{issueText}{debug}");
    }

    private static IReadOnlyList<string> ReadIssues(JsonNode json)
    {
        var issues = new List<string>();
        if (json["details"] is JsonArray details)
        {
            foreach (var detail in details.OfType<JsonObject>())
            {
                var issue = detail["issue"]?.GetValue<string>();
                var description = detail["description"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(issue) && !string.IsNullOrWhiteSpace(description))
                {
                    issues.Add($"{issue}: {description}");
                }
                else if (!string.IsNullOrWhiteSpace(issue))
                {
                    issues.Add(issue);
                }
            }
        }

        return issues;
    }

    private static string RequiredString(JsonNode json, string name)
    {
        var value = GetString(json, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw OrderPaymentException.Unprocessable($"PayPal response did not include '{name}'.");
        }

        return value;
    }

    private static string? GetString(JsonNode? node, params string[] path)
    {
        var current = Walk(node, path);
        if (current == null || current is JsonObject or JsonArray)
        {
            return null;
        }

        try
        {
            return current.GetValue<string>();
        }
        catch
        {
            return current.ToString();
        }
    }

    private static decimal GetDecimal(JsonNode? node, params string[] path) =>
        GetNullableDecimal(node, path) ?? 0m;

    private static decimal? GetNullableDecimal(JsonNode? node, params string[] path)
    {
        var current = Walk(node, path);
        if (current == null || current is JsonObject or JsonArray)
        {
            return null;
        }

        try
        {
            if (current is JsonValue value)
            {
                if (value.TryGetValue<decimal>(out var dec)) return dec;
                if (value.TryGetValue<string>(out var str) &&
                    decimal.TryParse(str, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static DateTimeOffset? GetDate(JsonNode? node, params string[] path)
    {
        var text = GetString(node, path);
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            return value;
        }

        return null;
    }

    private static JsonNode? Walk(JsonNode? node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            current = current?[segment];
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static string RedactSecrets(string json)
    {
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"number\"\\s*:\\s*\"[^\"]*\"", "\"number\":\"***\"");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"security_code\"\\s*:\\s*\"[^\"]*\"", "\"security_code\":\"***\"");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"client_secret\"\\s*:\\s*\"[^\"]*\"", "\"client_secret\":\"***\"");
        return json;
    }

    private static string LastFour(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static string ToPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value) =>
        value.Length <= 300 ? value : value[..300];
}
