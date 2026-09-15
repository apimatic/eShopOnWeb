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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalClient : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayPalCheckoutOrder> CreateAuthorizedCardOrderAsync(
        PayPalAuthorizeOrderRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = request.CustomId,
                    ["invoice_id"] = request.InvoiceId,
                    ["description"] = request.Description,
                    ["amount"] = new Dictionary<string, string>
                    {
                        ["currency_code"] = request.CurrencyCode,
                        ["value"] = request.Amount
                    }
                }
            }
        };

        var createdJson = await SendAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            createBody,
            payPalRequestId,
            cancellationToken);

        var created = createdJson.Deserialize<PayPalOrderDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty order response.", HttpStatusCode.BadGateway);

        EnsureNoPayerActionRequired(created);

        var authorizeBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardPaymentSource(request)
            }
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/checkout/orders/{created.Id}/authorize",
            authorizeBody,
            $"{payPalRequestId}-authorize",
            cancellationToken);

        var order = json.Deserialize<PayPalOrderDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty authorize response.", HttpStatusCode.BadGateway);

        EnsureNoPayerActionRequired(order);

        var authorization = ExtractAuthorization(order);
        if (authorization is null)
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for order {order.Id} (status {order.Status}).",
                HttpStatusCode.BadGateway,
                DebugIdFrom(json));
        }

        return new PayPalCheckoutOrder
        {
            Id = order.Id ?? created.Id!,
            Status = order.Status ?? string.Empty,
            Authorization = authorization,
            PayerActionUrl = FindLink(order.Links, "payer-action")
        };
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            payPalRequestId: null,
            cancellationToken);

        return MapAuthorization(json);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        string currencyCode,
        string amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currencyCode,
                ["value"] = amount
            }
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            payPalRequestId,
            cancellationToken);

        return MapAuthorization(json);
    }

    public async Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string currencyCode,
        string amount,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currencyCode,
                ["value"] = amount
            },
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            payPalRequestId,
            cancellationToken);

        return MapCapture(json);
    }

    public async Task<PayPalCaptureDetails> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/captures/{captureId}",
            body: null,
            payPalRequestId: null,
            cancellationToken);

        return MapCapture(json);
    }

    public Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            body: new Dictionary<string, object>(),
            payPalRequestId,
            cancellationToken);
    }

    public async Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        string? currencyCode,
        string? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body;
        if (!string.IsNullOrWhiteSpace(amount) && !string.IsNullOrWhiteSpace(currencyCode))
        {
            body = new Dictionary<string, object?>
            {
                ["amount"] = new Dictionary<string, string>
                {
                    ["currency_code"] = currencyCode,
                    ["value"] = amount
                }
            };
        }
        else
        {
            body = new Dictionary<string, object?>();
        }

        var json = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            payPalRequestId,
            cancellationToken);

        var dto = json.Deserialize<PayPalRefundDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty refund response.", HttpStatusCode.BadGateway);

        return new PayPalRefundDetails
        {
            Id = dto.Id ?? throw new PaymentException("PayPal refund was missing an id.", HttpStatusCode.BadGateway),
            Status = dto.Status ?? string.Empty,
            Amount = ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardDetails card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, string>
            {
                ["merchant_customer_id"] = merchantCustomerId
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?>
                {
                    ["name"] = card.Name,
                    ["number"] = card.Number,
                    ["expiry"] = card.Expiry,
                    ["security_code"] = card.SecurityCode,
                    ["billing_address"] = ToPayPalAddress(card.BillingAddress)
                }
            }
        };

        var json = await SendAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            payPalRequestId,
            cancellationToken);

        var dto = json.Deserialize<PayPalPaymentTokenDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty vault response.", HttpStatusCode.BadGateway);

        var cardResponse = dto.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            PaymentTokenId = dto.Id ?? throw new PaymentException("PayPal vault response was missing a token id.", HttpStatusCode.BadGateway),
            CustomerId = dto.Customer?.Id,
            LastDigits = cardResponse?.LastDigits,
            Brand = cardResponse?.Brand,
            Expiry = cardResponse?.Expiry,
            CardholderName = cardResponse?.Name
        };
    }

    public Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        return SendAsync(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            payPalRequestId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var rangeEnd = to.ToUniversalTime();

        while (windowStart < rangeEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > rangeEnd)
            {
                windowEnd = rangeEnd;
            }

            await AddWindowTransactionsAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task AddWindowTransactionsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatTimestamp(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatTimestamp(end))}" +
                $"&fields=all" +
                $"&page_size=500" +
                $"&page={page}" +
                $"&balance_affecting_records_only=N";

            var json = await SendAsync(HttpMethod.Get, query, body: null, payPalRequestId: null, cancellationToken);
            var dto = json.Deserialize<PayPalSearchResponseDto>(JsonOptions) ?? new PayPalSearchResponseDto();
            totalPages = dto.TotalPages is > 0 ? dto.TotalPages.Value : page;

            if (dto.TransactionDetails is not null)
            {
                foreach (var detail in dto.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        Status = info.TransactionStatus,
                        EventCode = info.TransactionEventCode,
                        Amount = info.TransactionAmount?.Value,
                        FeeAmount = info.FeeAmount?.Value,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        CustomField = info.CustomField,
                        InvoiceId = info.InvoiceId,
                        ReferenceId = info.PaypalReferenceId,
                        InitiationDate = ParseTimestamp(info.TransactionInitiationDate)
                    });
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private Dictionary<string, object?> BuildCardPaymentSource(PayPalAuthorizeOrderRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new Dictionary<string, object?>
            {
                ["vault_id"] = request.VaultId
            };
        }

        if (request.Card is null)
        {
            throw new PaymentException("A card or saved payment method is required to authorize payment.");
        }

        return new Dictionary<string, object?>
        {
            ["name"] = request.Card.Name,
            ["number"] = request.Card.Number,
            ["expiry"] = request.Card.Expiry,
            ["security_code"] = request.Card.SecurityCode,
            ["billing_address"] = ToPayPalAddress(request.Card.BillingAddress)
        };
    }

    private static Dictionary<string, string?> ToPayPalAddress(PayPalBillingAddress address) => new()
    {
        ["address_line_1"] = address.AddressLine1,
        ["address_line_2"] = address.AddressLine2,
        ["admin_area_2"] = address.AdminArea2,
        ["admin_area_1"] = address.AdminArea1,
        ["postal_code"] = address.PostalCode,
        ["country_code"] = address.CountryCode
    };

    private void EnsureNoPayerActionRequired(PayPalOrderDto order)
    {
        var payerAction = FindLink(order.Links, "payer-action");
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(payerAction))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper challenge (for example 3-D Secure) that needs a browser. Direct card processing cannot continue without that round-trip.",
                order.Id,
                debugId: null);
        }
    }

    private static PayPalAuthorizationDetails? ExtractAuthorization(PayPalOrderDto order)
    {
        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault(a => a.Id is not null);

        return authorization is null ? null : MapAuthorization(authorization);
    }

    private PayPalAuthorizationDetails MapAuthorization(JsonDocument json)
    {
        var dto = json.Deserialize<PayPalAuthorizationDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty authorization response.", HttpStatusCode.BadGateway);
        return MapAuthorization(dto);
    }

    private static PayPalAuthorizationDetails MapAuthorization(PayPalAuthorizationDto dto) => new()
    {
        Id = dto.Id ?? throw new PaymentException("PayPal authorization was missing an id."),
        Status = dto.Status ?? string.Empty,
        Amount = dto.Amount?.Value,
        Currency = dto.Amount?.CurrencyCode,
        ExpirationTime = ParseTimestamp(dto.ExpirationTime)
    };

    private PayPalCaptureDetails MapCapture(JsonDocument json)
    {
        var dto = json.Deserialize<PayPalCaptureDto>(JsonOptions)
            ?? throw new PaymentException("PayPal returned an empty capture response.", HttpStatusCode.BadGateway);

        return new PayPalCaptureDetails
        {
            Id = dto.Id ?? throw new PaymentException("PayPal capture was missing an id.", HttpStatusCode.BadGateway),
            Status = dto.Status ?? string.Empty,
            CapturedAmount = ParseMoneyNullable(dto.SellerReceivableBreakdown?.GrossAmount?.Value ?? dto.Amount?.Value),
            PaypalFee = ParseMoneyNullable(dto.SellerReceivableBreakdown?.PaypalFee?.Value),
            NetAmount = ParseMoneyNullable(dto.SellerReceivableBreakdown?.NetAmount?.Value),
            Currency = dto.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? dto.Amount?.CurrencyCode
        };
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? payPalRequestId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        Exception? lastException = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(payPalRequestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", payPalRequestId);
            }

            if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                var payload = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(payload, Encoding.UTF8);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastException = ex;
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode == 401 && attempt < 3)
            {
                _accessToken = null;
                _tokenExpiresAt = DateTimeOffset.MinValue;
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode == 429 || ((int)response.StatusCode >= 500 && attempt < 3))
            {
                _logger.LogWarning(
                    "PayPal {Method} {Url} returned {Status}. Retrying. debug_id may be in the body.",
                    method,
                    relativeUrl,
                    (int)response.StatusCode);
                await DelayRetryAsync(attempt, cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(raw))
            {
                if (response.IsSuccessStatusCode)
                {
                    return JsonDocument.Parse("{}");
                }

                throw new PaymentException(
                    $"PayPal request failed with status {(int)response.StatusCode}.",
                    MapStatus(response.StatusCode));
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                throw new PaymentException(
                    $"PayPal returned a non-JSON response with status {(int)response.StatusCode}.",
                    MapStatus(response.StatusCode));
            }

            if (!response.IsSuccessStatusCode)
            {
                ThrowPayPalError(document, response.StatusCode);
            }

            return document;
        }

        throw lastException ?? new PaymentException("PayPal request failed after retries.", HttpStatusCode.BadGateway);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {Status}.", (int)response.StatusCode);
                throw new PaymentException(
                    "Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.",
                    HttpStatusCode.BadGateway);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenDto>(raw, JsonOptions)
                ?? throw new PaymentException("PayPal token response was empty.", HttpStatusCode.BadGateway);

            _accessToken = token.AccessToken
                ?? throw new PaymentException("PayPal token response was missing access_token.", HttpStatusCode.BadGateway);
            var lifetime = token.ExpiresIn is > 0 ? TimeSpan.FromSeconds(token.ExpiresIn.Value) : TimeSpan.FromMinutes(5);
            _tokenExpiresAt = DateTimeOffset.UtcNow.Add(lifetime - TimeSpan.FromSeconds(60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new PaymentException(
                "PayPal is not configured. Set PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment, and PayPal:Currency.",
                HttpStatusCode.ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", HttpStatusCode.ServiceUnavailable);
        }
    }

    private static void ThrowPayPalError(JsonDocument document, HttpStatusCode statusCode)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var details = new List<string>();

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("name", out var nameEl))
            {
                name = nameEl.GetString();
            }

            if (document.RootElement.TryGetProperty("message", out var messageEl))
            {
                message = messageEl.GetString();
            }

            if (document.RootElement.TryGetProperty("debug_id", out var debugEl))
            {
                debugId = debugEl.GetString();
            }

            if (document.RootElement.TryGetProperty("details", out var detailsEl) &&
                detailsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsEl.EnumerateArray())
                {
                    var issue = detail.TryGetProperty("issue", out var issueEl) ? issueEl.GetString() : null;
                    var description = detail.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
                    var field = detail.TryGetProperty("field", out var fieldEl) ? fieldEl.GetString() : null;
                    var parts = new[] { issue, field is null ? null : $"field {field}", description }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var line = string.Join(": ", parts);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        details.Add(line);
                    }
                }
            }
        }

        var summary = string.IsNullOrWhiteSpace(message) ? name ?? "PayPal request failed." : message;
        if (details.Count > 0)
        {
            summary = $"{summary} ({string.Join("; ", details)})";
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            summary = $"{summary} [PayPal debug_id {debugId}]";
        }

        throw new PaymentException(summary, MapStatus(statusCode), debugId)
        {
            Data = { ["paypal_name"] = name ?? string.Empty }
        };
    }

    private static HttpStatusCode MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized => HttpStatusCode.BadGateway,
        HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
        HttpStatusCode.NotFound => HttpStatusCode.BadRequest,
        HttpStatusCode.Conflict => HttpStatusCode.Conflict,
        HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
        _ => HttpStatusCode.BadGateway
    };

    private static string? FindLink(IEnumerable<PayPalLinkDto>? links, string rel) =>
        links?.FirstOrDefault(l => string.Equals(l.Rel, rel, StringComparison.OrdinalIgnoreCase))?.Href;

    private static string? DebugIdFrom(JsonDocument json) =>
        json.RootElement.ValueKind == JsonValueKind.Object &&
        json.RootElement.TryGetProperty("debug_id", out var el)
            ? el.GetString()
            : null;

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    private static decimal? ParseMoneyNullable(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static async Task DelayRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 250 + Random.Shared.Next(0, 120));
        await Task.Delay(delayMs, cancellationToken);
    }

    private sealed class PayPalTokenDto
    {
        public string? AccessToken { get; set; }
        public int? ExpiresIn { get; set; }
    }

    private sealed class PayPalOrderDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PayPalLinkDto>? Links { get; set; }
        public List<PayPalPurchaseUnitDto>? PurchaseUnits { get; set; }
    }

    private sealed class PayPalPurchaseUnitDto
    {
        public PayPalPaymentCollectionDto? Payments { get; set; }
    }

    private sealed class PayPalPaymentCollectionDto
    {
        public List<PayPalAuthorizationDto>? Authorizations { get; set; }
    }

    private sealed class PayPalAuthorizationDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoneyDto? Amount { get; set; }
        public string? ExpirationTime { get; set; }
    }

    private sealed class PayPalCaptureDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoneyDto? Amount { get; set; }
        public PayPalReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    }

    private sealed class PayPalReceivableBreakdownDto
    {
        public PayPalMoneyDto? GrossAmount { get; set; }
        public PayPalMoneyDto? PaypalFee { get; set; }
        public PayPalMoneyDto? NetAmount { get; set; }
    }

    private sealed class PayPalRefundDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoneyDto? Amount { get; set; }
    }

    private sealed class PayPalPaymentTokenDto
    {
        public string? Id { get; set; }
        public PayPalCustomerDto? Customer { get; set; }
        public PayPalPaymentSourceDto? PaymentSource { get; set; }
    }

    private sealed class PayPalCustomerDto
    {
        public string? Id { get; set; }
        public string? MerchantCustomerId { get; set; }
    }

    private sealed class PayPalPaymentSourceDto
    {
        public PayPalCardResponseDto? Card { get; set; }
    }

    private sealed class PayPalCardResponseDto
    {
        public string? Name { get; set; }
        public string? LastDigits { get; set; }
        public string? Brand { get; set; }
        public string? Expiry { get; set; }
    }

    private sealed class PayPalSearchResponseDto
    {
        public List<PayPalTransactionDetailDto>? TransactionDetails { get; set; }
        public int? TotalPages { get; set; }
        public int? Page { get; set; }
    }

    private sealed class PayPalTransactionDetailDto
    {
        public PayPalTransactionInfoDto? TransactionInfo { get; set; }
    }

    private sealed class PayPalTransactionInfoDto
    {
        public string? TransactionId { get; set; }
        public string? TransactionStatus { get; set; }
        public string? TransactionEventCode { get; set; }
        public PayPalMoneyDto? TransactionAmount { get; set; }
        public PayPalMoneyDto? FeeAmount { get; set; }
        public string? CustomField { get; set; }
        public string? InvoiceId { get; set; }
        public string? PaypalReferenceId { get; set; }
        public string? TransactionInitiationDate { get; set; }
    }

    private sealed class PayPalMoneyDto
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private sealed class PayPalLinkDto
    {
        public string? Href { get; set; }
        public string? Rel { get; set; }
    }
}
