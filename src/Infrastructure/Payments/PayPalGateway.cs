using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new PaymentException("PayPal:Currency is not configured.", HttpStatusCode.InternalServerError);
            }

            return _options.Currency;
        }
    }

    public async Task<PayPalAuthorizedOrder> AuthorizeCardPaymentAsync(
        PayPalAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.CustomId,
                    ["description"] = request.Description ?? $"eShopOnWeb order {request.CustomId}",
                    ["amount"] = Amount(request.Amount, request.Currency)
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardSource(request)
            }
        };

        var created = await SendAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            payload,
            request.RequestId,
            cancellationToken);

        EnsureNoPayerAction(created);

        var orderId = RequiredString(created, "id");
        var orderStatus = RequiredString(created, "status");
        var authorization = TryReadAuthorization(created);

        if (authorization == null &&
            (string.Equals(orderStatus, "CREATED", StringComparison.OrdinalIgnoreCase)
             || string.Equals(orderStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
             || string.Equals(orderStatus, "SAVED", StringComparison.OrdinalIgnoreCase)))
        {
            var authorized = await SendAsync(
                HttpMethod.Post,
                $"v2/checkout/orders/{orderId}/authorize",
                new Dictionary<string, object?>(),
                request.RequestId + "-authorize",
                cancellationToken);

            EnsureNoPayerAction(authorized);
            orderStatus = RequiredString(authorized, "status");
            authorization = TryReadAuthorization(authorized)
                ?? throw new PaymentException("PayPal authorized the order but did not return an authorization id.", HttpStatusCode.BadGateway);
        }

        if (authorization == null)
        {
            throw new PaymentException(
                $"PayPal did not authorize the payment (order status '{orderStatus}').",
                HttpStatusCode.BadGateway);
        }

        return new PayPalAuthorizedOrder
        {
            OrderId = orderId,
            OrderStatus = orderStatus,
            Authorization = authorization
        };
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ReadAuthorization(json);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new Dictionary<string, object?> { ["amount"] = Amount(amount, currency) },
            requestId,
            cancellationToken);

        return ReadAuthorization(json);
    }

    public async Task<PayPalCapture> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new Dictionary<string, object?>
            {
                ["amount"] = Amount(amount, currency),
                ["final_capture"] = true
            },
            requestId,
            cancellationToken);

        var capture = ReadCapture(json);
        var detailed = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/captures/{capture.Id}",
            body: null,
            requestId: null,
            cancellationToken);
        return ReadCapture(detailed);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                new Dictionary<string, object?>(),
                requestId,
                cancellationToken);
        }
        catch (PaymentException ex) when (
            ex.StatusCode == HttpStatusCode.UnprocessableEntity
            && (string.Equals(ex.Issue, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.Issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefund> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = Amount(amount.Value, currency) }
            : new Dictionary<string, object?>();

        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        return new PayPalRefund
        {
            Id = RequiredString(json, "id"),
            Status = RequiredString(json, "status"),
            Amount = ReadMoney(json, "amount") ?? amount ?? 0m,
            CreateTime = ReadTime(json, "create_time")
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildRawCard(card)
            }
        };

        var setup = await SendAsync(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupPayload,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(setup);

        var setupStatus = RequiredString(setup, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal did not approve the card for vaulting (status '{setupStatus}').",
                HttpStatusCode.BadGateway);
        }

        var setupTokenId = RequiredString(setup, "id");
        var tokenPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var vaulted = await SendAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenPayload,
            requestId + "-token",
            cancellationToken);

        var cardNode = vaulted.TryGetProperty("payment_source", out var source)
                       && source.TryGetProperty("card", out var cardEl)
            ? cardEl
            : default;

        var lastDigits = cardNode.ValueKind == JsonValueKind.Object && cardNode.TryGetProperty("last_digits", out var last)
            ? last.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(lastDigits))
        {
            lastDigits = card.Number.Length >= 4 ? card.Number[^4..] : "****";
        }

        string? customerId = null;
        if (vaulted.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var customerIdEl))
        {
            customerId = customerIdEl.GetString();
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = RequiredString(vaulted, "id"),
            CustomerId = customerId,
            LastDigits = lastDigits,
            Brand = cardNode.ValueKind == JsonValueKind.Object && cardNode.TryGetProperty("brand", out var brand)
                ? brand.GetString()
                : null,
            Expiry = cardNode.ValueKind == JsonValueKind.Object && cardNode.TryGetProperty("expiry", out var expiry)
                ? expiry.GetString()
                : card.Expiry,
            Name = cardNode.ValueKind == JsonValueKind.Object && cardNode.TryGetProperty("name", out var name)
                ? name.GetString()
                : card.Name
        };
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                requestId: null,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("PayPal payment token was already deleted.");
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            if (windowEnd <= windowStart)
            {
                windowEnd = windowStart.AddSeconds(1);
            }

            await ListTransactionsInWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ListTransactionsInWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var start = Uri.EscapeDataString(FormatPayPalDate(from));
            var end = Uri.EscapeDataString(FormatPayPalDate(to));
            var path =
                $"v1/reporting/transactions?start_date={start}&end_date={end}&page={page}&page_size=500&fields=all&balance_affecting_records_only=N";

            JsonElement json;
            try
            {
                json = await SendAsync(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
            }
            catch (PaymentException ex) when (
                ex.StatusCode == HttpStatusCode.NotFound
                || (ex.Message?.Contains("start date is not available", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                _logger.LogInformation(
                    "PayPal reporting has no data for {From} to {To}: {Message}",
                    from,
                    to,
                    ex.Message);
                return;
            }

            if (json.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    results.Add(ReadReportedTransaction(detail));
                }
            }

            totalPages = json.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages)
                ? Math.Max(pages, 1)
                : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Calling PayPal {Method} {Path}", method.Method, SanitizePath(relativePath));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || (allowEmpty && string.IsNullOrWhiteSpace(responseBody)))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = PayPalApiException.FromResponse(response.StatusCode, responseBody);
            _logger.LogWarning(
                "PayPal {Method} {Path} failed with {Status} issue {Issue} debug {DebugId}",
                method.Method,
                SanitizePath(relativePath),
                (int)response.StatusCode,
                error.Issue,
                error.DebugId);
            throw new PaymentException(error.Message, error.StatusCode, error.Issue);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.Clone();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = PayPalApiException.FromResponse(response.StatusCode, body);
                throw new PaymentException(error.Message, error.StatusCode, error.Issue);
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new PaymentException("PayPal did not return an access token.", HttpStatusCode.BadGateway);
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt32(out var seconds)
                ? seconds
                : 300;

            _accessToken = token;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException(
                "PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret.",
                HttpStatusCode.InternalServerError);
        }
    }

    private static void EnsureNoPayerAction(JsonElement json)
    {
        var status = json.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval challenge in the browser (for example 3-D Secure). This integration does not collect money that way.");
        }
    }

    private static Dictionary<string, object?> BuildCardSource(PayPalAuthorizeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new Dictionary<string, object?> { ["vault_id"] = request.VaultId };
        }

        if (request.Card == null)
        {
            throw new PaymentException("A card or a saved payment method is required.");
        }

        return BuildRawCard(request.Card);
    }

    private static Dictionary<string, object?> BuildRawCard(PayPalCardDetails card)
    {
        var source = new Dictionary<string, object?>
        {
            ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            ["expiry"] = card.Expiry
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            source["security_code"] = card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            source["name"] = card.Name;
        }
        else
        {
            source["name"] = "Sandbox Shopper";
        }

        var billing = card.BillingAddress;
        var address = new Dictionary<string, object?>
        {
            ["address_line_1"] = string.IsNullOrWhiteSpace(billing?.AddressLine1) ? "2211 N First Street" : billing!.AddressLine1,
            ["admin_area_1"] = string.IsNullOrWhiteSpace(billing?.AdminArea1) ? "CA" : billing!.AdminArea1,
            ["admin_area_2"] = string.IsNullOrWhiteSpace(billing?.AdminArea2) ? "San Jose" : billing!.AdminArea2,
            ["postal_code"] = string.IsNullOrWhiteSpace(billing?.PostalCode) ? "95131" : billing!.PostalCode,
            ["country_code"] = string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode
        };
        if (!string.IsNullOrWhiteSpace(billing?.AddressLine2))
        {
            address["address_line_2"] = billing!.AddressLine2;
        }

        source["billing_address"] = address;

        return source;
    }

    private Dictionary<string, object?> Amount(decimal amount, string currency)
    {
        return new Dictionary<string, object?>
        {
            ["currency_code"] = currency,
            ["value"] = FormatAmount(amount, currency)
        };
    }

    private static string FormatAmount(decimal amount, string currency)
    {
        var rounded = ZeroDecimalCurrencies.Contains(currency)
            ? decimal.Round(amount, 0, MidpointRounding.AwayFromZero)
            : decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

        return ZeroDecimalCurrencies.Contains(currency)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static PayPalAuthorization? TryReadAuthorization(JsonElement json)
    {
        if (!json.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return json.TryGetProperty("id", out _) && json.TryGetProperty("status", out var status)
                   && json.TryGetProperty("expiration_time", out _)
                ? ReadAuthorization(json)
                : null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments)
                || !payments.TryGetProperty("authorizations", out var auths)
                || auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                return ReadAuthorization(auth);
            }
        }

        return null;
    }

    private static PayPalAuthorization ReadAuthorization(JsonElement json)
    {
        return new PayPalAuthorization
        {
            Id = RequiredString(json, "id"),
            Status = RequiredString(json, "status"),
            Amount = ReadMoney(json, "amount"),
            CreateTime = ReadTime(json, "create_time"),
            ExpirationTime = ReadTime(json, "expiration_time")
        };
    }

    private static PayPalCapture ReadCapture(JsonElement json)
    {
        decimal? fee = null;
        decimal? net = null;
        if (json.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }

        return new PayPalCapture
        {
            Id = RequiredString(json, "id"),
            Status = RequiredString(json, "status"),
            Amount = ReadMoney(json, "amount") ?? 0m,
            PayPalFee = fee,
            NetAmount = net,
            CreateTime = ReadTime(json, "create_time")
        };
    }

    private static PayPalReportedTransaction ReadReportedTransaction(JsonElement detail)
    {
        JsonElement info = detail;
        if (detail.TryGetProperty("transaction_info", out var nested))
        {
            info = nested;
        }

        return new PayPalReportedTransaction
        {
            TransactionId = ReadOptionalString(info, "transaction_id"),
            PaypalReferenceId = ReadOptionalString(info, "paypal_reference_id"),
            PaypalReferenceIdType = ReadOptionalString(info, "paypal_reference_id_type"),
            TransactionEventCode = ReadOptionalString(info, "transaction_event_code"),
            TransactionStatus = ReadOptionalString(info, "transaction_status"),
            InvoiceId = ReadOptionalString(info, "invoice_id"),
            CustomField = ReadOptionalString(info, "custom_field"),
            Amount = ReadMoney(info, "transaction_amount"),
            Currency = info.TryGetProperty("transaction_amount", out var amt) && amt.TryGetProperty("currency_code", out var cur)
                ? cur.GetString()
                : null,
            FeeAmount = ReadMoney(info, "fee_amount"),
            InitiationDate = ReadTime(info, "transaction_initiation_date"),
            UpdatedDate = ReadTime(info, "transaction_updated_date")
        };
    }

    private static string RequiredString(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            throw new PaymentException($"PayPal response was missing '{name}'.", HttpStatusCode.BadGateway);
        }

        return el.GetString() ?? throw new PaymentException($"PayPal response was missing '{name}'.", HttpStatusCode.BadGateway);
    }

    private static string? ReadOptionalString(JsonElement json, string name)
    {
        return json.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static decimal? ReadMoney(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var valueEl))
        {
            return decimal.TryParse(valueEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            return decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        return null;
    }

    private static DateTimeOffset? ReadTime(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatPayPalDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string SanitizePath(string path)
    {
        return path.Length <= 120 ? path : path[..120];
    }
}
