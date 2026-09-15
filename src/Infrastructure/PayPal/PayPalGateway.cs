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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    public const string HttpClientName = "PayPal";
    private const string TokenCacheKey = "paypal:access-token";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalOptions _options;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string requestId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildCardPaymentSource(card);
        return AuthorizeAsync(orderId, amount, currency, paymentSource, requestId, invoiceId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = new JsonObject
            {
                ["vault_id"] = vaultId
            }
        };
        return AuthorizeAsync(orderId, amount, currency, paymentSource, requestId, invoiceId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return MapAuthorizationDetails(dto);
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
            ["amount"] = Money(amount, currency)
        };

        try
        {
            var dto = await SendAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/reauthorize",
                body,
                requestId,
                cancellationToken,
                preferRepresentation: true);
            return MapAuthorizationDetails(dto);
        }
        catch (CheckoutException ex) when (ex.Code is "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_DENIED" or "AUTHORIZATION_VOIDED")
        {
            throw new CheckoutException(409,
                "The payment hold has expired and PayPal will not renew it. Ask the shopper to place and pay a new order, then fulfil that authorization.",
                "AUTHORIZATION_CANNOT_BE_RENEWED");
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
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
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

        if (dto.SellerReceivableBreakdown?.PaypalFee is null || dto.SellerReceivableBreakdown.NetAmount is null)
        {
            dto = await SendAsync<PayPalCaptureDto>(
                HttpMethod.Get,
                $"/v2/payments/captures/{dto.Id}",
                body: null,
                requestId: null,
                cancellationToken) ?? dto;
        }

        return MapCapture(dto, currency);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendWithoutBodyAsync(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                requestId,
                cancellationToken);
        }
        catch (CheckoutException ex) when (ex.Code is "AUTHORIZATION_ALREADY_VOIDED" or "AUTHORIZATION_VOIDED" or "RESOURCE_NOT_FOUND")
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already released.", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency)
        };

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new CheckoutException(502, "PayPal refund response did not include a refund id.", "PAYPAL_INVALID_RESPONSE");
        }

        return new PayPalRefundResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            ParseMoney(dto.Amount?.Value, amount),
            dto.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
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

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            $"{requestId}-setup",
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerAction(setup.Status, setup.Links);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(setup.Status, "VAULTED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(502,
                $"PayPal did not approve the saved card (status {setup.Status}).",
                "PAYPAL_VAULT_NOT_APPROVED");
        }

        if (string.IsNullOrWhiteSpace(setup.Id))
        {
            throw new CheckoutException(502, "PayPal setup-token response did not include an id.", "PAYPAL_INVALID_RESPONSE");
        }

        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            $"{requestId}-token",
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new CheckoutException(502, "PayPal payment-token response did not include an id.", "PAYPAL_INVALID_RESPONSE");
        }

        var last4 = token.PaymentSource?.Card?.LastDigits
                    ?? setup.PaymentSource?.Card?.LastDigits
                    ?? Last4FromNumber(card.Number);
        var brand = token.PaymentSource?.Card?.Brand
                    ?? setup.PaymentSource?.Card?.Brand
                    ?? "CARD";
        var expiry = token.PaymentSource?.Card?.Expiry
                     ?? setup.PaymentSource?.Card?.Expiry
                     ?? card.Expiry;
        var name = token.PaymentSource?.Card?.Name
                   ?? setup.PaymentSource?.Card?.Name
                   ?? card.Name;
        var customerId = token.Customer?.Id ?? setup.Customer?.Id;

        return new PayPalVaultedCard(token.Id, last4, brand, expiry, name, customerId);
    }

    public async Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendWithoutBodyAsync(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{paymentTokenId}",
                requestId: null,
                cancellationToken);
        }
        catch (CheckoutException ex) when (ex.Code is "RESOURCE_NOT_FOUND")
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (chunkFrom, chunkTo) in SplitDateRange(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatPayPalDate(chunkFrom);
                var end = FormatPayPalDate(chunkTo);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                totalPages = Math.Max(response.TotalPages, 1);
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<PayPalTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null || !seen.Add(info.TransactionId))
                    {
                        continue;
                    }

                    DateTimeOffset.TryParse(info.TransactionInitiationDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var initiated);

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseMoneyOrNull(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        string.IsNullOrWhiteSpace(info.TransactionInitiationDate) ? null : initiated));
                }

                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        JsonObject paymentSource,
        string requestId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = $"order-{orderId}",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerAction(order.Status, order.Links);

        var authorization = FirstAuthorization(order);
        if (authorization is null &&
            (string.Equals(order.Status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)))
        {
            order = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new JsonObject(),
                $"{requestId}-authorize",
                cancellationToken,
                preferRepresentation: true);
            EnsureNoPayerAction(order.Status, order.Links);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null || order.Id is null)
        {
            throw new CheckoutException(502, "PayPal did not return an authorization for the order.", "PAYPAL_INVALID_RESPONSE");
        }

        var authorizedAmount = ParseMoney(authorization.Amount?.Value, amount);
        return new PayPalAuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status ?? "CREATED",
            authorizedAmount,
            authorization.Amount?.CurrencyCode ?? currency,
            authorization.ExpirationTime,
            authorization.CreateTime);
    }

    private static JsonObject BuildCardPaymentSource(CardPaymentDetails card)
    {
        return new JsonObject
        {
            ["card"] = BuildCardObject(card)
        };
    }

    private static JsonObject BuildCardObject(CardPaymentDetails card)
    {
        var billing = new JsonObject
        {
            ["address_line_1"] = card.BillingAddress.AddressLine1,
            ["admin_area_1"] = card.BillingAddress.AdminArea1,
            ["admin_area_2"] = card.BillingAddress.AdminArea2,
            ["postal_code"] = card.BillingAddress.PostalCode,
            ["country_code"] = card.BillingAddress.CountryCode
        };
        if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine2))
        {
            billing["address_line_2"] = card.BillingAddress.AddressLine2;
        }

        return new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name,
            ["billing_address"] = billing
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        JsonNode? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        bool allowEmpty = false) where T : class
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        var token = await GetAccessTokenAsync(cancellationToken);
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

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        finally
        {
            // Card PAN/CVC live only in the request content; never log it.
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            if (allowEmpty || string.IsNullOrWhiteSpace(payload))
            {
                return Activator.CreateInstance<T>();
            }

            var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (parsed is null)
            {
                throw new CheckoutException(502, "PayPal returned an empty success payload.", "PAYPAL_INVALID_RESPONSE");
            }

            return parsed;
        }

        throw MapPayPalError(response.StatusCode, payload);
    }

    private async Task SendWithoutBodyAsync(
        HttpMethod method,
        string path,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        throw MapPayPalError(response.StatusCode, payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new CheckoutException(500, "PayPal credentials are not configured.", "PAYPAL_NOT_CONFIGURED");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
            throw MapPayPalError(response.StatusCode, payload);
        }

        var token = JsonSerializer.Deserialize<PayPalOAuthResponse>(payload, JsonOptions);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new CheckoutException(502, "PayPal token response did not include an access token.", "PAYPAL_INVALID_RESPONSE");
        }

        var lifetime = token.ExpiresIn > 60 ? TimeSpan.FromSeconds(token.ExpiresIn - 60) : TimeSpan.FromSeconds(30);
        _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
        return token.AccessToken;
    }

    private CheckoutException MapPayPalError(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // PayPal sometimes returns non-JSON on gateway failures.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description ?? error?.Message ?? "PayPal request failed.";
        var debug = string.IsNullOrWhiteSpace(error?.DebugId) ? string.Empty : $" (debug_id {error!.DebugId})";

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error?.Name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckoutException(409,
                "PayPal required a shopper approval step in the browser. This integration does not collect that round-trip.",
                "PAYER_ACTION_REQUIRED");
        }

        var code = issue ?? error?.Name ?? "PAYPAL_ERROR";
        var http = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        _logger.LogWarning("PayPal API error {Code}: {Description}{Debug}", code, description, debug);
        return new CheckoutException(http, $"{description}{debug}", code);
    }

    private static void EnsureNoPayerAction(string? status, IEnumerable<PayPalLink>? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(409,
                "PayPal required a shopper approval step in the browser. This integration does not collect that round-trip.",
                "PAYER_ACTION_REQUIRED");
        }

        if (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new CheckoutException(409,
                "PayPal required a shopper approval step in the browser. This integration does not collect that round-trip.",
                "PAYER_ACTION_REQUIRED");
        }
    }

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static PayPalAuthorizationDetails MapAuthorizationDetails(PayPalAuthorizationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new CheckoutException(502, "PayPal authorization response did not include an id.", "PAYPAL_INVALID_RESPONSE");
        }

        return new PayPalAuthorizationDetails(
            dto.Id,
            dto.Status ?? "CREATED",
            ParseMoney(dto.Amount?.Value, 0m),
            dto.Amount?.CurrencyCode ?? string.Empty,
            dto.ExpirationTime,
            dto.CreateTime);
    }

    private static PayPalCaptureResult MapCapture(PayPalCaptureDto dto, string fallbackCurrency)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new CheckoutException(502, "PayPal capture response did not include an id.", "PAYPAL_INVALID_RESPONSE");
        }

        var captured = ParseMoney(dto.SellerReceivableBreakdown?.GrossAmount?.Value ?? dto.Amount?.Value, 0m);
        return new PayPalCaptureResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            captured,
            ParseMoneyOrNull(dto.SellerReceivableBreakdown?.PaypalFee?.Value),
            ParseMoneyOrNull(dto.SellerReceivableBreakdown?.NetAmount?.Value),
            dto.Amount?.CurrencyCode ?? fallbackCurrency);
    }

    private static JsonObject Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? decimal.Round(parsed, 2, MidpointRounding.AwayFromZero)
            : fallback;

    private static decimal? ParseMoneyOrNull(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? decimal.Round(parsed, 2, MidpointRounding.AwayFromZero)
            : null;

    private static string Last4FromNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor <= to)
        {
            var chunkEnd = cursor.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (cursor, chunkEnd);
            if (chunkEnd >= to)
            {
                yield break;
            }

            cursor = chunkEnd.AddSeconds(1);
        }
    }

    public static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        var environment = options.Environment?.Trim();
        if (string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }
}
