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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex CardNumberPattern = new(@"\b[0-9]{13,19}\b", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalAccessTokenCache _tokenCache;

    public PayPalGateway(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger,
        PayPalAccessTokenCache tokenCache)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _tokenCache = tokenCache;
    }

    public string Currency
    {
        get
        {
            var currency = _options.Value.Currency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new PaymentException(500, "PayPal:Currency is not configured.");
            }

            return currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount,
        string invoiceId,
        string customId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceJson
        {
            Card = ToCardJson(card)
        };

        return AuthorizeAsync(amount, invoiceId, customId, paymentSource, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        decimal amount,
        string invoiceId,
        string customId,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceJson
        {
            Card = new PayPalCardJson { VaultId = vaultId }
        };

        return AuthorizeAsync(amount, invoiceId, customId, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync<PayPalAuthorizationJson>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        return ToAuthorizationDetails(json);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = Money(amount)
        };

        var json = await SendAsync<PayPalAuthorizationJson>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        return ToAuthorizationDetails(json);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = Money(amount),
            invoice_id = invoiceId,
            final_capture = true
        };

        var json = await SendAsync<PayPalCaptureJson>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        if (json?.Id is null)
        {
            throw new PaymentException(502, "PayPal capture succeeded but returned no capture id.");
        }

        var captured = ParseMoney(json.Amount) ?? ParseMoney(json.SellerReceivableBreakdown?.GrossAmount) ?? amount;
        var fee = ParseMoney(json.SellerReceivableBreakdown?.PaypalFee);
        var net = ParseMoney(json.SellerReceivableBreakdown?.NetAmount);

        return new PayPalCaptureResult(
            json.Id,
            json.Status ?? "COMPLETED",
            captured,
            fee,
            net,
            json.Amount?.CurrencyCode ?? Currency);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationJson>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: new { },
            requestId,
            preferRepresentation: true,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string invoiceId,
        string customId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new
            {
                amount = Money(amount.Value),
                invoice_id = invoiceId,
                custom_id = customId
            }
            : new
            {
                invoice_id = invoiceId,
                custom_id = customId
            };

        var json = await SendAsync<PayPalRefundJson>(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        if (json?.Id is null)
        {
            throw new PaymentException(502, "PayPal refund succeeded but returned no refund id.");
        }

        return new PayPalRefundResult(
            json.Id,
            json.Status ?? "COMPLETED",
            ParseMoney(json.Amount) ?? amount ?? 0m,
            json.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                card = ToCardJson(card)
            },
            customer = new
            {
                merchant_customer_id = merchantCustomerId
            }
        };

        var json = await SendAsync<PayPalVaultResponseJson>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerChallenge(json?.Status, json?.Links, json?.PaymentSource);

        if (string.IsNullOrWhiteSpace(json?.Id))
        {
            throw new PaymentException(502, "PayPal vaulted the card but returned no payment token id.");
        }

        var cardResponse = json.PaymentSource?.Card;
        return new PayPalVaultedCardResult(
            json.Id,
            json.Customer?.Id,
            cardResponse?.Brand,
            cardResponse?.LastDigits,
            cardResponse?.Expiry,
            cardResponse?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalVaultResponseJson>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
                body: null,
                requestId: null,
                preferRepresentation: false,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token was already deleted.");
        }
    }

    public async Task<PayPalTransactionPage> ListTransactionsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var startUtc = FormatTimestamp(start);
        var endUtc = FormatTimestamp(end);
        var query =
            $"start_date={Uri.EscapeDataString(startUtc)}" +
            $"&end_date={Uri.EscapeDataString(endUtc)}" +
            $"&page={page}" +
            $"&page_size={pageSize}" +
            "&fields=transaction_info" +
            "&balance_affecting_records_only=N";

        var json = await SendAsync<PayPalTransactionSearchJson>(
            HttpMethod.Get,
            $"/v1/reporting/transactions?{query}",
            body: null,
            requestId: null,
            preferRepresentation: false,
            cancellationToken);

        var transactions = (json?.TransactionDetails ?? new List<PayPalTransactionDetailJson>())
            .Select(detail => detail.TransactionInfo)
            .Where(info => info?.TransactionId != null)
            .Select(info => new PayPalReportedTransaction(
                info!.TransactionId!,
                info.PaypalReferenceId,
                info.InvoiceId,
                info.CustomField,
                info.TransactionEventCode,
                info.TransactionStatus,
                ParseAmountValue(info.TransactionAmount?.Value),
                info.TransactionAmount?.CurrencyCode,
                ParseTimestamp(info.TransactionInitiationDate),
                ParseAmountValue(info.FeeAmount?.Value)))
            .ToList();

        return new PayPalTransactionPage(transactions, page, json?.TotalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string invoiceId,
        string customId,
        PayPalPaymentSourceJson paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    invoice_id = invoiceId,
                    custom_id = customId,
                    amount = Money(amount)
                }
            },
            payment_source = paymentSource
        };

        var order = await SendAsync<PayPalOrderJson>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerChallenge(order?.Status, order?.Links, order?.PaymentSource);

        if (string.IsNullOrWhiteSpace(order?.Id))
        {
            throw new PaymentException(502, "PayPal created an order but returned no id.");
        }

        var authorization = FirstAuthorization(order);
        if (authorization?.Id is null)
        {
            order = await SendAsync<PayPalOrderJson>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{Uri.EscapeDataString(order.Id)}/authorize",
                new { },
                $"{requestId}-authorize",
                preferRepresentation: true,
                cancellationToken);

            EnsureNoPayerChallenge(order?.Status, order?.Links, order?.PaymentSource);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException(502, "PayPal authorized the payment but returned no authorization id.");
        }

        var expiration = ParseTimestamp(authorization.ExpirationTime);
        if (expiration is null)
        {
            var details = await GetAuthorizationAsync(authorization.Id, cancellationToken);
            expiration = details.ExpirationTime;
            authorization.Status = details.Status;
        }

        return new PayPalAuthorizationResult(
            order!.Id!,
            authorization.Id,
            authorization.Status ?? "CREATED",
            expiration,
            ParseMoney(authorization.Amount) ?? amount,
            authorization.Amount?.CurrencyCode ?? Currency);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        EnsureConfigured();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0 && response?.StatusCode == HttpStatusCode.Unauthorized, cancellationToken);
            using var request = new HttpRequestMessage(method, CombineUrl(relativePath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body != null && method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogInformation("PayPal access token was rejected; requesting a new token.");
                continue;
            }

            if ((int)response.StatusCode == 429 && attempt < 2)
            {
                await DelayForRetryAsync(response, attempt, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < 2)
            {
                await DelayForRetryAsync(response, attempt, cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new PaymentException(502, "No response was received from PayPal.");
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (allowEmpty || string.IsNullOrWhiteSpace(content))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }

            throw ToPaymentException(response.StatusCode, content);
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _tokenCache.AccessToken != null && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt)
        {
            return _tokenCache.AccessToken;
        }

        await _tokenCache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _tokenCache.AccessToken != null && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt)
            {
                return _tokenCache.AccessToken;
            }

            var options = _options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToPaymentException(response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenJson>(content, JsonOptions);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                throw new PaymentException(502, "PayPal token response did not include an access token.");
            }

            var lifetime = token.ExpiresIn > 0 ? token.ExpiresIn : 300;
            _tokenCache.AccessToken = token.AccessToken;
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, lifetime - 60));
            _logger.LogInformation("Obtained a PayPal access token.");
            return _tokenCache.AccessToken;
        }
        finally
        {
            _tokenCache.Gate.Release();
        }
    }

    private void EnsureConfigured()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new PaymentException(500, "PayPal client credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
        }

        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            throw new PaymentException(500, "PayPal:Currency is not configured.");
        }
    }

    private string CombineUrl(string relativePath)
    {
        return _options.Value.ResolveBaseUrl() + relativePath;
    }

    private object Money(decimal amount)
    {
        return new
        {
            currency_code = Currency,
            value = FormatAmount(amount, Currency)
        };
    }

    private PayPalCardJson ToCardJson(CardPaymentSource card)
    {
        return new PayPalCardJson
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalAddressJson
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalAuthorizationJson? FirstAuthorization(PayPalOrderJson? order)
    {
        return order?.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
    }

    private PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationJson? json)
    {
        if (json?.Id is null)
        {
            throw new PaymentException(502, "PayPal authorization details were missing an id.");
        }

        return new PayPalAuthorizationDetails(
            json.Id,
            json.Status ?? "UNKNOWN",
            ParseTimestamp(json.ExpirationTime),
            ParseMoney(json.Amount) ?? 0m,
            json.Amount?.CurrencyCode ?? Currency);
    }

    private static void EnsureNoPayerChallenge(string? status, IEnumerable<PayPalLinkJson>? links, PayPalPaymentSourceJson? paymentSource)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal required a shopper challenge (for example 3-D Secure) that cannot be completed through this API. No browser approval flow is implemented.");
        }

        if (links?.Any(link =>
                link.Rel != null &&
                link.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal returned a payer-action link. A shopper would need to approve this payment in a browser, which this API does not support.");
        }

        var authenticationStatus = paymentSource?.Card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus;
        if (string.Equals(authenticationStatus, "C", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requested a 3-D Secure challenge. A shopper would need to complete it in a browser, which this API does not support.");
        }
    }

    private PaymentException ToPaymentException(HttpStatusCode statusCode, string content)
    {
        var redacted = Redact(content);
        PayPalErrorJson? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorJson>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // The body is not a PayPal error document; use the raw (redacted) text.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var message = error?.Message ?? error?.Name;
        var parts = new[] { message, issue, description, error?.DebugId is null ? null : $"debug_id={error.DebugId}" }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var combined = string.Join(". ", parts);

        if (string.IsNullOrWhiteSpace(combined))
        {
            combined = string.IsNullOrWhiteSpace(redacted)
                ? $"PayPal request failed with HTTP {(int)statusCode}."
                : $"PayPal request failed with HTTP {(int)statusCode}: {redacted}";
        }
        else
        {
            combined = $"PayPal request failed with HTTP {(int)statusCode}: {Redact(combined)}";
        }

        var mapped = (int)statusCode switch
        {
            400 or 404 or 409 or 422 => (int)statusCode,
            401 or 403 => 502,
            >= 500 => 502,
            _ => 400
        };

        _logger.LogWarning("PayPal API call failed: {Message}", combined);
        return new PaymentException(mapped, combined);
    }

    private static async Task DelayForRetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            delay = retryAfter;
        }

        await Task.Delay(delay, cancellationToken);
    }

    internal static string FormatAmount(decimal amount, string currency)
    {
        var decimals = IsZeroDecimalCurrency(currency) ? 0 : 2;
        return decimal.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static bool IsZeroDecimalCurrency(string currency)
    {
        return currency.ToUpperInvariant() is "JPY" or "HUF" or "TWD" or "KRW";
    }

    private static decimal? ParseMoney(PayPalMoneyJson? money) => ParseAmountValue(money?.Value);

    private static decimal? ParseAmountValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string Redact(string value)
    {
        return CardNumberPattern.Replace(value, "************");
    }
}
