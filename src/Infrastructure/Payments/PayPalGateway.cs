using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new PaymentException("PayPal:Currency is not configured.", 500);
            }

            return _options.Currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildCardPaymentSource(card);
        return AuthorizeAsync(amount, currency, invoiceId, requestId, paymentSource, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?>
            {
                ["vault_id"] = vaultId
            }
        };
        return AuthorizeAsync(amount, currency, invoiceId, requestId, paymentSource, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        var dto = Deserialize<PayPalAuthorizationDto>(response);
        return ToAuthorizationDetails(dto);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new
            {
                currency_code = currency,
                value = MoneyFormat.ToPayPalValue(amount)
            }
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var dto = Deserialize<PayPalAuthorizationDto>(response);
        return ToAuthorizationDetails(dto);
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
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                requestId,
                cancellationToken);
        }
        catch (PaymentException ex) when (
            string.Equals(ex.PayPalIssue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.PayPalIssue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent: already released.
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new
            {
                currency_code = currency,
                value = MoneyFormat.ToPayPalValue(amount)
            },
            invoice_id = Truncate(invoiceId, 127),
            final_capture = true
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var dto = Deserialize<PayPalCaptureDto>(response);
        if (dto.SellerReceivableBreakdown?.PaypalFee == null || dto.SellerReceivableBreakdown.NetAmount == null)
        {
            dto = await GetCaptureAsync(dto.Id!, cancellationToken);
        }

        return ToCaptureResult(dto);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new
            {
                currency_code = currency,
                value = MoneyFormat.ToPayPalValue(amount)
            }
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var dto = Deserialize<PayPalRefundDto>(response);
        return new PayPalRefundResult(
            dto.Id ?? throw Missing("refund id"),
            dto.Status ?? "COMPLETED",
            MoneyFormat.Parse(dto.Amount?.Value),
            dto.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? existingCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(existingCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = existingCustomerId };
        }

        setupBody["payment_source"] = new Dictionary<string, object?>
        {
            ["card"] = BuildCardObject(card, includeSecurityCode: true, includeExperienceContext: true)
        };

        var setupRaw = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            requestId,
            cancellationToken);

        var setup = Deserialize<PayPalSetupTokenResponse>(setupRaw);
        EnsureNoPayerAction(setup.Status, setup.Links);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal did not approve the card for vaulting (status {setup.Status}).",
                502);
        }

        var tokenRaw = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new
                    {
                        id = setup.Id,
                        type = "SETUP_TOKEN"
                    }
                }
            },
            requestId + "-token",
            cancellationToken);

        var token = Deserialize<PayPalPaymentTokenResponse>(tokenRaw);
        var cardInfo = token.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        return new PayPalVaultedCard(
            token.Id ?? throw Missing("vault id"),
            cardInfo?.LastDigits ?? LastFour(card.Number),
            cardInfo?.Brand ?? "CARD",
            cardInfo?.Expiry ?? card.Expiry,
            cardInfo?.Name ?? card.Name,
            token.Customer?.Id ?? setup.Customer?.Id ?? existingCustomerId);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to, TimeSpan.FromDays(31)))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                var start = FormatReportingDate(windowStart);
                var end = FormatReportingDate(windowEnd);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}";

                try
                {
                    var raw = await SendAsync(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
                    var pageResult = Deserialize<PayPalTransactionSearchResponse>(raw);
                    totalPages = Math.Max(pageResult.TotalPages, 1);

                    if (pageResult.TransactionDetails != null)
                    {
                        foreach (var detail in pageResult.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            if (info == null)
                            {
                                continue;
                            }

                            results.Add(new PayPalReportedTransaction(
                                info.TransactionId ?? string.Empty,
                                info.PaypalReferenceId,
                                info.InvoiceId,
                                info.CustomField,
                                info.TransactionEventCode,
                                info.TransactionStatus,
                                info.TransactionAmount?.Value,
                                info.TransactionAmount?.CurrencyCode,
                                info.TransactionInitiationDate));
                        }
                    }

                    page++;
                }
                catch (PaymentException ex) when (
                    ex.StatusCode == 404
                    || ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ex.PayPalIssue, "DATA_NOT_AVAILABLE", StringComparison.OrdinalIgnoreCase))
                {
                    // Transaction Search lags live activity in sandbox; an empty window is a valid report.
                    _logger.LogInformation("PayPal reporting has no data for {Start}–{End}: {Message}", start, end, ex.Message);
                    break;
                }
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        object paymentSource,
        CancellationToken cancellationToken)
    {
        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = Truncate(invoiceId, 127),
                    ["custom_id"] = Truncate(invoiceId, 127),
                    ["amount"] = new Dictionary<string, object?>
                    {
                        ["currency_code"] = currency,
                        ["value"] = MoneyFormat.ToPayPalValue(amount)
                    }
                }
            },
            ["payment_source"] = paymentSource
        };

        var createRaw = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var order = Deserialize<PayPalOrderResponse>(createRaw);
        EnsureNoPayerAction(order.Status, order.Links);

        var authorization = FirstAuthorization(order);
        if (authorization == null)
        {
            var authorizeRaw = await SendAsync(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new { },
                requestId + "-authorize",
                cancellationToken,
                preferRepresentation: true);

            order = Deserialize<PayPalOrderResponse>(authorizeRaw);
            EnsureNoPayerAction(order.Status, order.Links);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id == null)
        {
            throw new PaymentException(
                "PayPal did not return an authorization id for the card payment.",
                502);
        }

        return new PayPalAuthorizationResult(
            order.Id ?? throw Missing("PayPal order id"),
            authorization.Id,
            authorization.Status ?? "CREATED",
            authorization.ExpirationTime,
            MoneyFormat.Parse(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? currency);
    }

    private async Task<PayPalCaptureDto> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            body: null,
            requestId: null,
            cancellationToken);
        return Deserialize<PayPalCaptureDto>(raw);
    }

    private static PayPalCaptureResult ToCaptureResult(PayPalCaptureDto dto)
    {
        var captured = MoneyFormat.Parse(dto.SellerReceivableBreakdown?.GrossAmount?.Value ?? dto.Amount?.Value);
        var fee = MoneyFormat.Parse(dto.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = dto.SellerReceivableBreakdown?.NetAmount != null
            ? MoneyFormat.Parse(dto.SellerReceivableBreakdown.NetAmount.Value)
            : captured - fee;

        return new PayPalCaptureResult(
            dto.Id ?? throw Missing("capture id"),
            dto.Status ?? "COMPLETED",
            captured,
            fee,
            net,
            dto.Amount?.CurrencyCode ?? dto.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? string.Empty);
    }

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto dto) =>
        new(
            dto.Id ?? throw Missing("authorization id"),
            dto.Status ?? string.Empty,
            dto.CreateTime,
            dto.ExpirationTime,
            MoneyFormat.Parse(dto.Amount?.Value),
            dto.Amount?.CurrencyCode ?? string.Empty);

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits?.Count > 0 ? order.PurchaseUnits[0].Payments?.Authorizations?[0] : null;

    private static Dictionary<string, object?> BuildCardPaymentSource(CardPaymentSource card) =>
        new()
        {
            ["card"] = BuildCardObject(card, includeSecurityCode: true, includeExperienceContext: false)
        };

    private static Dictionary<string, object?> BuildCardObject(
        CardPaymentSource card,
        bool includeSecurityCode,
        bool includeExperienceContext)
    {
        var number = NormalizeCardNumber(card.Number);
        var expiry = NormalizeExpiry(card.Expiry);
        var payload = new Dictionary<string, object?>
        {
            ["number"] = number,
            ["expiry"] = expiry,
            ["name"] = card.Name
        };

        if (includeSecurityCode && !string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            payload["security_code"] = card.SecurityCode.Trim();
        }

        var address = card.BillingAddress ?? new CardBillingAddress(
            "123 Main St",
            null,
            "San Jose",
            "CA",
            "95131",
            "US");

        payload["billing_address"] = new Dictionary<string, object?>
        {
            ["address_line_1"] = address.AddressLine1,
            ["address_line_2"] = address.AddressLine2,
            ["admin_area_2"] = address.AdminArea2,
            ["admin_area_1"] = address.AdminArea1,
            ["postal_code"] = address.PostalCode,
            ["country_code"] = address.CountryCode
        };

        if (includeExperienceContext)
        {
            payload["experience_context"] = new Dictionary<string, object?>
            {
                ["brand_name"] = "eShopOnWeb",
                ["locale"] = "en-US",
                ["return_url"] = "https://example.com/returnUrl",
                ["cancel_url"] = "https://example.com/cancelUrl"
            };
        }

        return payload;
    }

    private static string NormalizeCardNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentException("Card number is required.", 400);
        }

        return number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new PaymentException("Card expiry is required (YYYY-MM).", 400);
        }

        expiry = expiry.Trim();
        if (expiry.Length == 7 && expiry[4] == '-')
        {
            return expiry;
        }

        if (expiry.Length == 7 && expiry[2] == '/')
        {
            return $"{expiry[3..]}-{expiry[..2]}";
        }

        throw new PaymentException("Card expiry must be in YYYY-MM format.", 400);
    }

    private static string LastFour(string number)
    {
        var digits = NormalizeCardNumber(number);
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLinkDto>? links)
    {
        var payerAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (links != null && links.Exists(l =>
                string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)));

        if (payerAction)
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to complete a browser challenge (3-D Secure or payer approval). This integration does not perform that round-trip.");
        }
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var client = CreateClient();
        using var request = new HttpRequestMessage(method, path);

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
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal HTTP call failed for {Method} {Path}", method.Method, RedactPath(path));
            throw new PaymentException("Unable to reach PayPal.", 502);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return payload;
        }

        throw ToPaymentException(response.StatusCode, payload);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        var baseUrl = _options.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        return client;
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

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException("PayPal client credentials are not configured.", 500);
            }

            var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST /v1/oauth2/token");
            var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToPaymentException(response.StatusCode, payload);
            }

            var token = Deserialize<PayPalTokenResponse>(payload);
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentException("PayPal did not return an access token.", 502);
            }

            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PaymentException ToPaymentException(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Body is not a PayPal error document.
        }

        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : error?.Name;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : error?.Message;
        var message = string.IsNullOrWhiteSpace(description)
            ? $"PayPal request failed with {(int)statusCode}."
            : description;

        var mapped = statusCode switch
        {
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.UnprocessableEntity => 422,
            HttpStatusCode.BadRequest => 400,
            _ => 502
        };

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PayerActionRequiredException(message, error?.DebugId);
        }

        return new PaymentException(message, mapped, error?.DebugId, issue);
    }

    private static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new PaymentException("PayPal returned an empty response.", 502);
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new PaymentException("PayPal returned an unreadable response.", 502);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxWindow)
    {
        var cursor = from;
        var max = maxWindow.Subtract(TimeSpan.FromSeconds(1));
        while (cursor < to)
        {
            var end = cursor + max;
            if (end > to)
            {
                end = to;
            }

            yield return (cursor, end);
            cursor = end.AddSeconds(1);
        }

        if (cursor == from && from == to)
        {
            yield return (from, to);
        }
    }

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string RedactPath(string path) => path;

    private static Exception Missing(string name) =>
        new PaymentException($"PayPal response was missing {name}.", 502);
}
