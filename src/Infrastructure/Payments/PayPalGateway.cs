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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenCache _tokenCache;
    private readonly IOptionsMonitor<PayPalOptions> _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        HttpClient httpClient,
        PayPalAccessTokenCache tokenCache,
        IOptionsMonitor<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _options = options;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string Currency
    {
        get
        {
            var currency = _options.CurrentValue.Currency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new InvalidPaymentRequestException("PayPal:Currency is not configured.");
            }

            return currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string requestId,
        string invoiceId,
        string customId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        PayPalCardSource card,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.Name,
                billing_address = ToBillingAddress(card.BillingAddress)
            }
        };

        return AuthorizeAsync(requestId, invoiceId, customId, amount, items, paymentSource, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string requestId,
        string invoiceId,
        string customId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            }
        };

        return AuthorizeAsync(requestId, invoiceId, customId, amount, items, paymentSource, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return ParseAuthorizationDetails(json);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string requestId,
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { amount = Money(amount) }, JsonOptions);
        var json = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken);
        return ParseAuthorizationDetails(json);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string requestId,
        string authorizationId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            invoice_id = invoiceId,
            final_capture = true
        }, JsonOptions);

        try
        {
            var json = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken);
            return ParseCapture(json);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode is 409 or 422)
        {
            var details = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (!string.IsNullOrEmpty(details.CaptureId))
            {
                var captureJson = await SendAsync(HttpMethod.Get, $"/v2/payments/captures/{details.CaptureId}", null, null, cancellationToken);
                return ParseCapture(captureJson);
            }

            throw;
        }
    }

    public async Task VoidAuthorizationAsync(string requestId, string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", "{}", requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string requestId,
        string captureId,
        decimal? amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var body = amount is null
            ? "{}"
            : JsonSerializer.Serialize(new { amount = Money(amount.Value, currency) }, JsonOptions);

        var json = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        var root = ParseObject(json);
        var refundAmount = ReadMoney(root, "amount") ?? amount ?? 0m;
        return new PayPalRefundResult(
            Required(root, "id"),
            ReadString(root, "status") ?? "COMPLETED",
            refundAmount,
            ReadString(root["amount"] as JsonObject, "currency_code") ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        string requestId,
        string merchantCustomerId,
        PayPalCardSource card,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = ToBillingAddress(card.BillingAddress)
                }
            },
            customer = new
            {
                merchant_customer_id = merchantCustomerId
            }
        };

        var json = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", JsonSerializer.Serialize(payload, JsonOptions), requestId, cancellationToken);
        var root = ParseObject(json);
        var cardNode = root["payment_source"]?["card"] as JsonObject;
        var lastDigits = ReadString(cardNode, "last_digits");
        if (string.IsNullOrEmpty(lastDigits) && card.Number.Length >= 4)
        {
            lastDigits = card.Number[^4..];
        }

        return new PayPalVaultedCard(
            Required(root, "id"),
            ReadString(root["customer"] as JsonObject, "id"),
            lastDigits ?? "0000",
            ReadString(cardNode, "brand"),
            ReadString(cardNode, "expiry") ?? card.Expiry,
            ReadString(cardNode, "name") ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        while (windowStart <= end)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            await ListTransactionsForWindowAsync(windowStart, windowEnd, results, cancellationToken);
            if (windowEnd == end)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task ListTransactionsForWindowAsync(
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
                $"start_date={Uri.EscapeDataString(FormatTime(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatTime(to))}" +
                $"&fields=transaction_info" +
                $"&page_size=100" +
                $"&page={page}" +
                $"&balance_affecting_records_only=N";

            JsonObject root;
            try
            {
                var json = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null, null, cancellationToken);
                root = ParseObject(json);
            }
            catch (PayPalGatewayException ex) when (IsReportingDataUnavailable(ex))
            {
                // Transaction Search lags live activity and rejects ranges with no available data.
                return;
            }
            catch (PayPalGatewayException ex) when (page == 1 && ex.StatusCode == 400 && query.Contains("balance_affecting_records_only=N"))
            {
                query = query.Replace("balance_affecting_records_only=N", "balance_affecting_records_only=Y");
                try
                {
                    var json = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null, null, cancellationToken);
                    root = ParseObject(json);
                }
                catch (PayPalGatewayException retryEx) when (IsReportingDataUnavailable(retryEx))
                {
                    return;
                }
            }

            if (root["transaction_details"] is JsonArray details)
            {
                foreach (var item in details)
                {
                    if (item is not JsonObject detail)
                    {
                        continue;
                    }

                    var info = detail["transaction_info"] as JsonObject;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        ReadString(info, "transaction_id") ?? string.Empty,
                        ReadString(info, "paypal_reference_id"),
                        ReadString(info, "invoice_id"),
                        ReadString(info, "custom_field"),
                        ReadString(info, "transaction_status"),
                        ReadMoney(info, "transaction_amount"),
                        ReadString(info["transaction_amount"] as JsonObject, "currency_code"),
                        ReadTime(info, "transaction_initiation_date") ?? ReadTime(info, "transaction_updated_date"),
                        ReadString(info, "transaction_event_code")));
                }
            }

            totalPages = root["total_pages"]?.GetValue<int>() ?? page;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        string requestId,
        string invoiceId,
        string customId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        object paymentSource,
        CancellationToken cancellationToken)
    {
        var orderBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = customId,
                    invoice_id = invoiceId,
                    amount = new
                    {
                        currency_code = Currency,
                        value = FormatAmount(amount),
                        breakdown = new
                        {
                            item_total = Money(amount)
                        }
                    },
                    items = items.Select(i => new
                    {
                        name = Truncate(i.Name, 127),
                        quantity = i.Quantity.ToString(CultureInfo.InvariantCulture),
                        unit_amount = Money(i.UnitAmount),
                        category = "PHYSICAL_GOODS"
                    }).ToArray()
                }
            },
            payment_source = paymentSource
        };

        var json = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            JsonSerializer.Serialize(orderBody, JsonOptions),
            requestId,
            cancellationToken);

        var root = ParseObject(json);
        EnsureNoPayerAction(root);

        var authorization = FindAuthorization(root);
        if (authorization is null)
        {
            var status = ReadString(root, "status");
            if (status is "CREATED" or "APPROVED" or "PAYER_ACTION_REQUIRED")
            {
                if (status == "PAYER_ACTION_REQUIRED")
                {
                    EnsureNoPayerAction(root);
                }

                json = await SendAsync(
                    HttpMethod.Post,
                    $"/v2/checkout/orders/{Required(root, "id")}/authorize",
                    "{}",
                    requestId + "-authorize",
                    cancellationToken);
                root = ParseObject(json);
                EnsureNoPayerAction(root);
                authorization = FindAuthorization(root);
            }
        }

        if (authorization is null)
        {
            throw new PayPalGatewayException(
                "PayPal did not return an authorization for this card payment.",
                502,
                ReadString(root, "debug_id"));
        }

        var authAmount = ReadMoney(authorization, "amount") ?? amount;
        return new PayPalAuthorizationResult(
            Required(root, "id"),
            ReadString(root, "status") ?? "AUTHORIZED",
            Required(authorization, "id"),
            ReadString(authorization, "status") ?? "CREATED",
            authAmount,
            ReadString(authorization["amount"] as JsonObject, "currency_code") ?? Currency,
            ReadTime(authorization, "expiration_time"));
    }

    private static void EnsureNoPayerAction(JsonObject root)
    {
        var status = ReadString(root, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step in the browser (3-D Secure or similar). This integration does not collect a browser round-trip.");
        }

        if (root["links"] is JsonArray links)
        {
            foreach (var link in links)
            {
                var rel = ReadString(link as JsonObject, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayerActionRequiredException(
                        "PayPal required a shopper approval step in the browser (3-D Secure or similar). This integration does not collect a browser round-trip.");
                }
            }
        }
    }

    private static JsonObject? FindAuthorization(JsonObject root)
    {
        if (root["purchase_units"] is not JsonArray units)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit?["payments"]?["authorizations"] as JsonArray;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                if (authorization is JsonObject obj && !string.IsNullOrEmpty(ReadString(obj, "id")))
                {
                    return obj;
                }
            }
        }

        return null;
    }

    private PayPalAuthorizationDetails ParseAuthorizationDetails(string json)
    {
        var root = ParseObject(json);
        var captureId = (root["supplementary_data"]?["related_ids"] as JsonObject)?["capture_id"]?.GetValue<string>();
        return new PayPalAuthorizationDetails(
            Required(root, "id"),
            ReadString(root, "status") ?? "CREATED",
            ReadMoney(root, "amount") ?? 0m,
            ReadString(root["amount"] as JsonObject, "currency_code") ?? Currency,
            ReadTime(root, "expiration_time"),
            ReadTime(root, "create_time"),
            captureId);
    }

    private PayPalCaptureResult ParseCapture(string json)
    {
        var root = ParseObject(json);
        var breakdown = root["seller_receivable_breakdown"] as JsonObject;
        return new PayPalCaptureResult(
            Required(root, "id"),
            ReadString(root, "status") ?? "COMPLETED",
            ReadMoney(root, "amount") ?? ReadMoney(breakdown, "gross_amount") ?? 0m,
            ReadMoney(breakdown, "paypal_fee"),
            ReadMoney(breakdown, "net_amount"),
            ReadString(root["amount"] as JsonObject, "currency_code") ?? Currency);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? jsonBody,
        string? requestId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        Exception? lastException = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(method, CombineUrl(relativeUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (jsonBody is not null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < 3)
            {
                lastException = ex;
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent)
            {
                return payload;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                _logger.LogWarning("PayPal rate-limited the request. Retrying with backoff. Debug id may be in the body.");
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < 3 && (method == HttpMethod.Get || requestId is not null))
            {
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            throw ToGatewayException(response.StatusCode, payload);
        }

        throw lastException ?? new PayPalGatewayException("PayPal request failed after retries.", 502);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return await _tokenCache.GetTokenAsync(async ct =>
        {
            var options = _options.CurrentValue;
            using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException(response.StatusCode, payload);
            }

            var root = ParseObject(payload);
            var token = Required(root, "access_token");
            var expiresIn = root["expires_in"]?.GetValue<int>() ?? 300;
            return (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30)));
        }, cancellationToken);
    }

    private void EnsureConfigured()
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidPaymentRequestException("PayPal client credentials are not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            throw new InvalidPaymentRequestException("PayPal:Currency is not configured.");
        }
    }

    private string CombineUrl(string relativeUrl)
    {
        var baseUrl = ResolveBaseUrl().TrimEnd('/');
        if (!relativeUrl.StartsWith('/'))
        {
            relativeUrl = "/" + relativeUrl;
        }

        return baseUrl + relativeUrl;
    }

    private string ResolveBaseUrl()
    {
        var options = _options.CurrentValue;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.Trim();
        }

        return string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    private static bool IsReportingDataUnavailable(PayPalGatewayException exception) =>
        exception.StatusCode is 400 or 404
        && (exception.Message.Contains("not available", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("start date", StringComparison.OrdinalIgnoreCase));

    private static async Task DelayRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 200 + Random.Shared.Next(0, 100));
        await Task.Delay(delayMs, cancellationToken);
    }

    private PayPalGatewayException ToGatewayException(HttpStatusCode statusCode, string payload)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        try
        {
            var root = JsonNode.Parse(payload) as JsonObject;
            name = ReadString(root, "name");
            message = ReadString(root, "message");
            debugId = ReadString(root, "debug_id");
            if (root?["details"] is JsonArray details)
            {
                foreach (var detail in details)
                {
                    var issue = ReadString(detail as JsonObject, "issue");
                    var description = ReadString(detail as JsonObject, "description");
                    if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(description))
                    {
                        message = string.IsNullOrEmpty(message)
                            ? $"{issue}: {description}"
                            : $"{message} ({issue}: {description})";
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Body is not JSON; fall through to a generic message.
        }

        _logger.LogError("PayPal API error {StatusCode} {Name} debug_id={DebugId}", (int)statusCode, name, debugId);
        return new PayPalGatewayException(
            message ?? $"PayPal request failed with status {(int)statusCode}.",
            (int)statusCode,
            debugId,
            name);
    }

    private static object? ToBillingAddress(PayPalBillingAddress? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.CountryCode))
        {
            return null;
        }

        return new
        {
            country_code = address.CountryCode,
            address_line_1 = address.AddressLine1,
            address_line_2 = address.AddressLine2,
            admin_area_2 = address.AdminArea2,
            admin_area_1 = address.AdminArea1,
            postal_code = address.PostalCode
        };
    }

    private object Money(decimal amount) => Money(amount, Currency);

    private static object Money(decimal amount, string currency) =>
        new { currency_code = currency, value = FormatAmount(amount) };

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static JsonObject ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(json) as JsonObject ?? throw new PayPalGatewayException("PayPal returned a non-object JSON body.", 502);
    }

    private static string Required(JsonObject root, string name) =>
        ReadString(root, name) ?? throw new PayPalGatewayException($"PayPal response was missing '{name}'.", 502);

    private static string? ReadString(JsonObject? obj, string name) => obj?[name]?.GetValue<string>();

    private static decimal? ReadMoney(JsonObject? obj, string name)
    {
        var node = obj?[name];
        if (node is JsonObject money)
        {
            var value = ReadString(money, "value");
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return Math.Round(parsed, 2, MidpointRounding.AwayFromZero);
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadTime(JsonObject? obj, string name)
    {
        var value = ReadString(obj, name);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
