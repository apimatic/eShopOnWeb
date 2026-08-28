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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> CreateOrderAsync(
        int orderId,
        decimal amount,
        string currency,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var formattedAmount = FormatAmount(amount);
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    amount = new { currency_code = currency, value = formattedAmount }
                }
            }
        };

        var created = await PostJsonAsync<PayPalOrderDto>(
            "/v2/checkout/orders",
            createBody,
            $"{invoiceId}-create",
            cancellationToken);
        return Required(created.Id, "order ID");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId,
        int orderId,
        decimal amount,
        string currency,
        CardInput? card,
        string? vaultId,
        int authorizationAttempt,
        CancellationToken cancellationToken)
    {
        if ((card is null) == string.IsNullOrWhiteSpace(vaultId))
        {
            throw new ArgumentException("Provide either card details or a vault ID, but not both.");
        }

        object paymentSource = card is not null
            ? new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = ToPayPalAddress(card.BillingAddress)
                }
            }
            : new { card = new { vault_id = vaultId } };

        PayPalOrderDto authorized;
        try
        {
            authorized = await PostJsonAsync<PayPalOrderDto>(
                $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
                new { payment_source = paymentSource },
                $"eshop-{payPalOrderId}-authorize-{authorizationAttempt}",
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.HasIssue("ORDER_ALREADY_AUTHORIZED"))
        {
            authorized = await GetJsonAsync<PayPalOrderDto>(
                $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}",
                cancellationToken);
        }

        EnsureNoApprovalRoundTrip(authorized.Status, authorized.Links);
        return ToAuthorizationResult(authorized, amount, currency);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string payPalOrderId,
        decimal amount,
        string currency,
        DateTimeOffset originalExpirationTime,
        string requestId,
        CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync<AuthorizationResponseDto>(
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = new { currency_code = currency, value = FormatAmount(amount) } },
            requestId,
            cancellationToken);

        var resultAmount = ParseMoney(response.Amount, amount);
        return new PayPalAuthorizationResult(
            payPalOrderId,
            Required(response.Id, "reauthorization ID"),
            Required(response.Status, "reauthorization status"),
            resultAmount,
            response.Amount?.CurrencyCode ?? currency,
            response.CreateTime ?? DateTimeOffset.UtcNow,
            response.ExpirationTime ?? originalExpirationTime,
            null,
            null);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync<CaptureResponseDto>(
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                invoice_id = invoiceId,
                amount = new { currency_code = currency, value = FormatAmount(amount) },
                final_capture = true
            },
            requestId,
            cancellationToken);

        return ToCaptureResult(response, amount, currency);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<CaptureResponseDto>(
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            cancellationToken);
        return ToCaptureResult(response, 0m, _options.Currency.ToUpperInvariant());
    }

    public async Task<string> VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                () => CreateJsonRequest(
                    HttpMethod.Post,
                    $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
                    new { }),
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return "VOIDED";
        }
        catch (PayPalApiException ex) when (ex.HasIssue("AUTHORIZATION_VOIDED"))
        {
            return "VOIDED";
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync<RefundResponseDto>(
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = new { currency_code = currency, value = FormatAmount(amount) } },
            requestId,
            cancellationToken);

        return new PayPalRefundResult(
            Required(response.Id, "refund ID"),
            Required(response.Status, "refund status"),
            ParseMoney(response.Amount, amount),
            response.Amount?.CurrencyCode ?? currency,
            response.CreateTime ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(
        CardInput card,
        string merchantCustomerId,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken)
    {
        object customer = string.IsNullOrWhiteSpace(payPalCustomerId)
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = payPalCustomerId };

        var setup = await PostJsonAsync<VaultTokenResponseDto>(
            "/v3/vault/setup-tokens",
            new
            {
                customer,
                payment_source = new
                {
                    card = new
                    {
                        number = card.Number,
                        expiry = card.Expiry,
                        security_code = card.SecurityCode,
                        name = card.Name,
                        billing_address = ToPayPalAddress(card.BillingAddress)
                    }
                }
            },
            $"{requestId}-setup",
            cancellationToken);

        EnsureNoApprovalRoundTrip(setup.Status, setup.Links);
        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(
                HttpStatusCode.UnprocessableEntity,
                $"PayPal did not approve the card for vaulting (status: {setup.Status}).");
        }

        var token = await PostJsonAsync<VaultTokenResponseDto>(
            "/v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new { id = setup.Id, type = "SETUP_TOKEN" }
                }
            },
            $"{requestId}-token",
            cancellationToken);

        var responseCard = token.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        return new PayPalSavedCardResult(
            Required(token.Id, "payment token ID"),
            token.Customer?.Id ?? setup.Customer?.Id,
            Required(responseCard?.Brand, "card brand"),
            Required(responseCard?.LastDigits, "card last digits"),
            Required(responseCard?.Expiry, "card expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                BuildUri($"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}")),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        CancellationToken cancellationToken)
    {
        var start = Uri.EscapeDataString(ToPayPalTimestamp(from));
        var end = Uri.EscapeDataString(ToPayPalTimestamp(to));
        var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}&fields=transaction_info&page_size=500&page={page}";
        var response = await GetJsonAsync<TransactionSearchResponseDto>(path, cancellationToken, enforceIso8601: true);
        var transactions = response.TransactionDetails.Select(x =>
        {
            var info = x.TransactionInfo;
            return new PayPalTransaction(
                info.TransactionId,
                info.PayPalReferenceId,
                info.PayPalReferenceIdType,
                info.TransactionEventCode,
                info.TransactionStatus,
                info.TransactionInitiationDate,
                info.TransactionUpdatedDate,
                ParseMoney(info.TransactionAmount, 0m),
                info.TransactionAmount?.CurrencyCode ?? string.Empty,
                info.FeeAmount is null ? null : ParseMoney(info.FeeAmount, 0m),
                info.InvoiceId);
        }).ToList();

        return new PayPalTransactionPage(transactions, response.Page, response.TotalPages);
    }

    private async Task<T> PostJsonAsync<T>(
        string path,
        object body,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateJsonRequest(HttpMethod.Post, path, body);
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            return request;
        }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        CancellationToken cancellationToken,
        bool enforceIso8601 = false)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
            if (enforceIso8601)
            {
                request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
            }
            return request;
        }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt == 1)
            {
                return response;
            }

            response.Dispose();
            _accessToken = null;
        }

        throw new InvalidOperationException("Unreachable PayPal request state.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            _options.Validate();
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var token = await DeserializeAsync<AccessTokenResponseDto>(response, cancellationToken);
            _accessToken = Required(token.AccessToken, "access token");
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object body)
    {
        return new HttpRequestMessage(method, BuildUri(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
    }

    private Uri BuildUri(string path)
    {
        return new Uri($"{_options.GetBaseUrl().TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var name = "PAYPAL_ERROR";
        var message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("name", out var nameElement) || root.TryGetProperty("error", out nameElement))
            {
                name = nameElement.GetString() ?? name;
            }
            if (root.TryGetProperty("message", out var messageElement) ||
                root.TryGetProperty("error_description", out messageElement))
            {
                message = messageElement.GetString() ?? message;
            }
            if (root.TryGetProperty("debug_id", out var debugElement))
            {
                debugId = debugElement.GetString();
            }
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var issue))
                    {
                        issues.Add(issue.GetString() ?? string.Empty);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // PayPal occasionally returns an HTML proxy error. Do not surface the raw body.
        }

        throw new PayPalApiException(response.StatusCode, name, message, debugId, issues);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new PaymentApiException(
            HttpStatusCode.BadGateway,
            "PayPal returned an empty or invalid response.");
    }

    private static PayPalAuthorizationResult ToAuthorizationResult(
        PayPalOrderDto order,
        decimal expectedAmount,
        string expectedCurrency)
    {
        var authorization = order.PurchaseUnits
            .SelectMany(x => x.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationResponseDto>())
            .SingleOrDefault()
            ?? throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal did not return an authorization.");

        var amount = ParseMoney(authorization.Amount, expectedAmount);
        var currency = authorization.Amount?.CurrencyCode ?? expectedCurrency;
        if (amount != expectedAmount || !string.Equals(currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal authorized an unexpected amount or currency.");
        }

        return new PayPalAuthorizationResult(
            Required(order.Id, "order ID"),
            Required(authorization.Id, "authorization ID"),
            Required(authorization.Status, "authorization status"),
            amount,
            currency,
            authorization.CreateTime ?? DateTimeOffset.UtcNow,
            authorization.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(29),
            order.PaymentSource?.Card?.Brand,
            order.PaymentSource?.Card?.LastDigits);
    }

    private static PayPalCaptureResult ToCaptureResult(
        CaptureResponseDto response,
        decimal fallbackAmount,
        string fallbackCurrency)
    {
        var amount = ParseMoney(response.Amount, fallbackAmount);
        var currency = response.Amount?.CurrencyCode ?? fallbackCurrency;
        var breakdown = response.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            Required(response.Id, "capture ID"),
            Required(response.Status, "capture status"),
            amount,
            currency,
            breakdown?.PayPalFee is null ? null : ParseMoney(breakdown.PayPalFee, 0m),
            breakdown?.NetAmount is null ? null : ParseMoney(breakdown.NetAmount, 0m),
            response.CreateTime ?? DateTimeOffset.UtcNow);
    }

    private static void EnsureNoApprovalRoundTrip(string status, IReadOnlyCollection<PayPalLinkDto> links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            links.Any(x => string.Equals(x.Rel, "approve", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(x.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PaymentApiException(
                HttpStatusCode.Conflict,
                "PayPal requires an interactive cardholder challenge; this headless payment flow cannot continue.");
        }
    }

    private static object ToPayPalAddress(BillingAddressInput address) => new
    {
        address_line_1 = address.AddressLine1,
        address_line_2 = address.AddressLine2,
        admin_area_2 = address.AdminArea2,
        admin_area_1 = address.AdminArea1,
        postal_code = address.PostalCode,
        country_code = address.CountryCode.ToUpperInvariant()
    };

    private static decimal ParseMoney(MoneyDto? money, decimal fallback)
    {
        if (money is null)
        {
            return fallback;
        }

        if (!decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal returned an invalid monetary amount.");
        }

        return amount;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string ToPayPalTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Required(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new PaymentApiException(HttpStatusCode.BadGateway, $"PayPal did not return a {fieldName}.")
            : value;

    private sealed class AccessTokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
