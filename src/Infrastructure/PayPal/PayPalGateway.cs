using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? CachedAccessToken;
    private static DateTimeOffset TokenExpiresAt;
    private static readonly Regex PanPattern = new(@"\b[0-9]{13,19}\b", RegexOptions.Compiled);
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency.Trim().ToUpperInvariant();

    public Task<AuthorizePaymentResult> AuthorizeCardAsync(
        decimal amount,
        string invoiceId,
        string customId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = BuildCardObject(card)
        };
        return AuthorizeAsync(amount, invoiceId, customId, paymentSource, requestId, cancellationToken);
    }

    public Task<AuthorizePaymentResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string invoiceId,
        string customId,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        };
        return AuthorizeAsync(amount, invoiceId, customId, paymentSource, requestId, cancellationToken);
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);
        return ToSnapshot(dto);
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyObject(amount)
        };
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);
        return ToSnapshot(dto);
    }

    public async Task<CapturePaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyObject(amount),
            ["final_capture"] = true,
            ["invoice_id"] = invoiceId
        };
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PayPalGatewayException("PayPal capture response did not include a capture id.");
        }

        var captured = ParseAmount(dto.SellerReceivableBreakdown?.GrossAmount?.Value) ?? ParseAmount(dto.Amount?.Value) ?? amount;
        var fee = ParseAmount(dto.SellerReceivableBreakdown?.PaypalFee?.Value) ?? 0m;
        var net = ParseAmount(dto.SellerReceivableBreakdown?.NetAmount?.Value) ?? (captured - fee);

        return new CapturePaymentResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            captured,
            fee,
            net,
            dto.Amount?.CurrencyCode ?? Currency,
            ParseTime(dto.CreateTime) ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<JsonElement?>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: null,
                requestId,
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (string.Equals(ex.Issue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent: already released.
        }
    }

    public async Task<RefundPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?>? body = null;
        if (amount.HasValue)
        {
            body = new Dictionary<string, object?>
            {
                ["amount"] = MoneyObject(amount.Value)
            };
        }

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PayPalGatewayException("PayPal refund response did not include a refund id.");
        }

        return new RefundPaymentResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            ParseAmount(dto.Amount?.Value) ?? amount ?? 0m,
            dto.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardObject(card)
            }
        };
        if (!string.IsNullOrWhiteSpace(paypalCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = paypalCustomerId };
        }

        var setup = await SendAsync<PayPalSetupTokenDto>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(setup.Status, setup.Links);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(setup.Status) &&
            !string.Equals(setup.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalGatewayException($"PayPal did not approve the card for vaulting (status {setup.Status}).");
        }

        if (string.IsNullOrWhiteSpace(setup.Id))
        {
            throw new PayPalGatewayException("PayPal setup-token response did not include an id.");
        }

        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            requestId + "-token",
            cancellationToken);

        EnsureNoPayerAction(null, token.Links);

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new PayPalGatewayException("PayPal payment-token response did not include an id.");
        }

        var lastDigits = token.PaymentSource?.Card?.LastDigits
            ?? setup.PaymentSource?.Card?.LastDigits
            ?? LastDigitsOf(card.Number);

        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id ?? paypalCustomerId,
            lastDigits,
            token.PaymentSource?.Card?.Brand ?? setup.PaymentSource?.Card?.Brand ?? "CARD",
            token.PaymentSource?.Card?.Expiry ?? setup.PaymentSource?.Card?.Expiry ?? card.Expiry,
            token.PaymentSource?.Card?.Name ?? setup.PaymentSource?.Card?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<JsonElement?>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
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
            windowStart = windowEnd.AddSeconds(1);
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
            var start = FormatReportingDate(from);
            var end = FormatReportingDate(to);
            var path =
                $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

            PayPalTransactionSearchDto pageResult;
            try
            {
                pageResult = await SendAsync<PayPalTransactionSearchDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);
            }
            catch (ResourceNotFoundException ex)
            {
                // Sandbox reporting lags live activity and may have no indexed data for a recent window.
                _logger.LogInformation(ex, "PayPal transaction search returned no data for {From} to {To}.", from, to);
                return;
            }

            if (pageResult.TransactionDetails != null)
            {
                foreach (var detail in pageResult.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null || string.IsNullOrWhiteSpace(info.TransactionId))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        info.TransactionEventCode,
                        info.InvoiceId,
                        info.CustomField,
                        info.PaypalReferenceId,
                        info.TransactionAmount?.Value,
                        info.TransactionAmount?.CurrencyCode,
                        ParseTime(info.TransactionInitiationDate)));
                }
            }

            totalPages = pageResult.TotalPages > 0 ? pageResult.TotalPages : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<AuthorizePaymentResult> AuthorizeAsync(
        decimal amount,
        string invoiceId,
        string customId,
        Dictionary<string, object?> paymentSource,
        string requestId,
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
                    ["amount"] = MoneyObject(amount)
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerAction(order.Status, order.Links);

        var authorization = FirstAuthorization(order);
        if (authorization == null &&
            (string.Equals(order.Status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(order.Id))
        {
            order = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                body: null,
                requestId: requestId + "-auth",
                cancellationToken,
                preferRepresentation: true);
            EnsureNoPayerAction(order.Status, order.Links);
            authorization = FirstAuthorization(order);
        }

        if (authorization == null || string.IsNullOrWhiteSpace(authorization.Id) || string.IsNullOrWhiteSpace(order.Id))
        {
            throw new PayPalGatewayException("PayPal did not return an authorization for the order.");
        }

        return new AuthorizePaymentResult(
            order.Id,
            authorization.Id,
            authorization.Status ?? order.Status ?? "CREATED",
            ParseTime(authorization.CreateTime),
            ParseTime(authorization.ExpirationTime),
            authorization.Amount?.CurrencyCode ?? Currency);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = BuildRequest(method, path, body, requestId, preferRepresentation, token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PayPalGatewayException($"PayPal request failed: {ex.Message}", HttpStatusCode.BadGateway);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            token = await GetAccessTokenAsync(cancellationToken);
            using var retry = BuildRequest(method, path, body, requestId, preferRepresentation, token);
            response.Dispose();
            response = await _httpClient.SendAsync(retry, cancellationToken);
        }

        if ((int)response.StatusCode == 409)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            InvalidateToken();
            token = await GetAccessTokenAsync(cancellationToken);
            using var retry = BuildRequest(method, path, body, requestId, preferRepresentation, token);
            response.Dispose();
            response = await _httpClient.SendAsync(retry, cancellationToken);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
        {
            if (response.IsSuccessStatusCode)
            {
                return default!;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateGatewayException(response.StatusCode, payload);
        }

        if (typeof(T) == typeof(JsonElement?) || payload.Length == 0)
        {
            return default!;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            return parsed ?? throw new PayPalGatewayException("PayPal returned an empty JSON payload.");
        }
        catch (JsonException ex)
        {
            _logger.LogError("Failed to parse PayPal response: {Message}", ex.Message);
            throw new PayPalGatewayException("PayPal returned a response that could not be parsed.");
        }
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool preferRepresentation,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, Combine(_options.ResolveBaseUrl(), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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

        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(CachedAccessToken) && TokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return CachedAccessToken;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(CachedAccessToken) && TokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return CachedAccessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PayPalGatewayException("PayPal ClientId and ClientSecret are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Combine(_options.ResolveBaseUrl(), "/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateGatewayException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthResponse>(payload, JsonOptions);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new PayPalGatewayException("PayPal OAuth response did not include an access token.");
            }

            CachedAccessToken = token.AccessToken;
            TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 300);
            return CachedAccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static void InvalidateToken()
    {
        CachedAccessToken = null;
        TokenExpiresAt = DateTimeOffset.MinValue;
    }

    private Exception CreateGatewayException(HttpStatusCode statusCode, string payload)
    {
        var redacted = Redact(payload);
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // fall through with raw payload
        }

        var issue = error?.Details is { Length: > 0 } ? error.Details[0].Issue : error?.Name;
        var description = error?.Details is { Length: > 0 }
            ? error.Details[0].Description
            : error?.Message;
        var message = string.IsNullOrWhiteSpace(description)
            ? $"PayPal request failed ({(int)statusCode})."
            : $"PayPal request failed ({issue ?? error?.Name}): {description}";

        _logger.LogWarning("PayPal API error {StatusCode} debugId={DebugId} issue={Issue} body={Body}",
            (int)statusCode, error?.DebugId, issue, redacted);

        if (statusCode == HttpStatusCode.NotFound)
        {
            return new ResourceNotFoundException(message);
        }

        var mapped = statusCode == HttpStatusCode.UnprocessableEntity ? HttpStatusCode.Conflict : statusCode;
        if ((int)mapped < 400 || (int)mapped > 599)
        {
            mapped = HttpStatusCode.BadGateway;
        }

        return new PayPalGatewayException(message, mapped, error?.DebugId, issue);
    }

    private static void EnsureNoPayerAction(string? status, PayPalLinkDto[]? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException();
        }

        if (links == null)
        {
            return;
        }

        foreach (var link in links)
        {
            if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException();
            }
        }
    }

    private Dictionary<string, object?> BuildCardObject(CardPaymentSource card)
    {
        var cardObject = new Dictionary<string, object?>
        {
            ["number"] = NormalizePan(card.Number),
            ["expiry"] = NormalizeExpiry(card.Expiry)
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            cardObject["security_code"] = card.SecurityCode.Trim();
        }
        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            cardObject["name"] = card.Name.Trim();
        }
        if (card.BillingAddress != null)
        {
            var address = new Dictionary<string, object?>
            {
                ["country_code"] = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                    ? "US"
                    : card.BillingAddress.CountryCode.Trim()
            };
            AddIfPresent(address, "address_line_1", card.BillingAddress.AddressLine1);
            AddIfPresent(address, "address_line_2", card.BillingAddress.AddressLine2);
            AddIfPresent(address, "admin_area_2", card.BillingAddress.AdminArea2);
            AddIfPresent(address, "admin_area_1", card.BillingAddress.AdminArea1);
            AddIfPresent(address, "postal_code", card.BillingAddress.PostalCode);
            cardObject["billing_address"] = address;
        }
        else
        {
            cardObject["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = "2211 N First Street",
                ["admin_area_2"] = "San Jose",
                ["admin_area_1"] = "CA",
                ["postal_code"] = "95131",
                ["country_code"] = "US"
            };
        }

        return cardObject;
    }

    private Dictionary<string, object?> MoneyObject(decimal amount) =>
        new()
        {
            ["currency_code"] = Currency,
            ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderDto order)
    {
        var units = order.PurchaseUnits;
        if (units == null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Length: > 0 } && !string.IsNullOrWhiteSpace(authorizations[0].Id))
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static AuthorizationSnapshot ToSnapshot(PayPalAuthorizationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PayPalGatewayException("PayPal authorization response did not include an id.");
        }

        return new AuthorizationSnapshot(
            dto.Id,
            dto.Status ?? "CREATED",
            ParseTime(dto.CreateTime),
            ParseTime(dto.ExpirationTime),
            dto.Amount?.Value,
            dto.Amount?.CurrencyCode);
    }

    private static string NormalizePan(string number) =>
        Regex.Replace(number ?? string.Empty, @"\s+", string.Empty);

    private static string NormalizeExpiry(string expiry)
    {
        var value = (expiry ?? string.Empty).Trim();
        if (Regex.IsMatch(value, @"^\d{4}-\d{2}$"))
        {
            return value;
        }

        var slash = Regex.Match(value, @"^(\d{1,2})\s*/\s*(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = slash.Groups[2].Value;
            if (year.Length == 2)
            {
                year = "20" + year;
            }
            return $"{year}-{month:00}";
        }

        return value;
    }

    private static string LastDigitsOf(string number)
    {
        var pan = NormalizePan(number);
        return pan.Length >= 4 ? pan[^4..] : pan;
    }

    private static void AddIfPresent(Dictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
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

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Combine(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var withoutPan = PanPattern.Replace(value, "************");
        withoutPan = Regex.Replace(withoutPan, "\"security_code\"\\s*:\\s*\"[^\"]+\"", "\"security_code\":\"***\"");
        withoutPan = Regex.Replace(withoutPan, "\"number\"\\s*:\\s*\"[^\"]+\"", "\"number\":\"************\"");
        return withoutPan;
    }
}
