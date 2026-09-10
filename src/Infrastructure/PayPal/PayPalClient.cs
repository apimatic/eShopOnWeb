using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Talks to PayPal's REST API over plain HTTP. Base address, credentials, environment and currency
/// come from <see cref="PayPalSettings"/> (never hard-coded). Card numbers are only ever sent in a
/// request body to PayPal and are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private const int ReportingPageSize = 500;
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient http, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    public async Task<AuthorizationResult> AuthorizeOrderWithCardAsync(
        decimal amount, string currency, CardDetails card, string customId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = BuildCreateOrderRequest(amount, currency, customId, new PaymentSourceModel
        {
            Card = new CardModel
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = ToBillingAddress(card.BillingAddress)
            }
        });
        return await CreateAuthorizationAsync(request, idempotencyKey, ct);
    }

    public async Task<AuthorizationResult> AuthorizeOrderWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string customId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = BuildCreateOrderRequest(amount, currency, customId, new PaymentSourceModel
        {
            Card = new CardModel { VaultId = vaultId }
        });
        return await CreateAuthorizationAsync(request, idempotencyKey, ct);
    }

    private static CreateOrderRequest BuildCreateOrderRequest(decimal amount, string currency, string customId, PaymentSourceModel source) => new()
    {
        Intent = "AUTHORIZE",
        PurchaseUnits = new List<PurchaseUnitRequest>
        {
            new()
            {
                Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
                CustomId = customId
            }
        },
        PaymentSource = source
    };

    private async Task<AuthorizationResult> CreateAuthorizationAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/checkout/orders");
        message.Content = JsonContent.Create(request, options: JsonOptions);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        var order = await SendAsync<CreateOrderResponse>(message, "create authorization order", ct);

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || HasPayerActionLink(order.Links))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration does not perform browser approval round-trips.");
        }

        var authorization = FindAuthorization(order);
        if (authorization?.Id is null)
        {
            throw new PayPalApiException(
                $"PayPal did not return an authorization for the order (status {order.Status}).", 502);
        }

        return new AuthorizationResult(order.Id!, authorization.Id, authorization.Status ?? "CREATED", authorization.ExpirationTime);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
            FinalCapture = true
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payments/authorizations/{authorizationId}/capture");
        message.Content = JsonContent.Create(body, options: JsonOptions);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        var capture = await SendAsync<CaptureResponse>(message, "capture authorization", ct);
        if (capture.Id is null)
        {
            throw new PayPalApiException("PayPal returned no capture id.", 502);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount, amount);
        var fee = ParseMoney(breakdown?.PaypalFee, 0m);
        var net = ParseMoney(breakdown?.NetAmount, gross - fee);
        return new CaptureResult(capture.Id, capture.Status ?? "COMPLETED", gross, fee, net);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        var body = new AmountOnlyRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payments/authorizations/{authorizationId}/reauthorize");
        message.Content = JsonContent.Create(body, options: JsonOptions);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        var reauth = await SendAsync<ReauthorizeResponse>(message, "reauthorize authorization", ct);
        if (reauth.Id is null)
        {
            throw new PayPalApiException("PayPal returned no reauthorization id.", 502);
        }
        return new ReauthorizeResult(reauth.Id, reauth.Status ?? "CREATED", reauth.ExpirationTime);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payments/authorizations/{authorizationId}/void");
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        await SendAsync(message, "void authorization", ct);
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new RefundRequest();
        if (amount.HasValue)
        {
            body.Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) };
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payments/captures/{captureId}/refund");
        message.Content = JsonContent.Create(body, options: JsonOptions);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        var refund = await SendAsync<RefundResponse>(message, "refund capture", ct);
        if (refund.Id is null)
        {
            throw new PayPalApiException("PayPal returned no refund id.", 502);
        }
        return new RefundResult(refund.Id, refund.Status ?? "COMPLETED");
    }

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new VaultTokenRequest
        {
            PaymentSource = new PaymentSourceModel
            {
                Card = new CardModel
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToBillingAddress(card.BillingAddress)
                }
            }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v3/vault/payment-tokens");
        message.Content = JsonContent.Create(body, options: JsonOptions);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        var token = await SendAsync<VaultTokenResponse>(message, "vault card", ct);
        var responseCard = token.PaymentSource?.Card;
        if (token.Id is null || responseCard is null)
        {
            throw new PayPalApiException("PayPal returned no vault token.", 502);
        }
        return new VaultCardResult(
            token.Id,
            responseCard.Brand ?? "CARD",
            responseCard.LastDigits ?? "****",
            responseCard.Expiry ?? card.Expiry,
            responseCard.Name ?? card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/v3/vault/payment-tokens/{vaultId}");
        await SendAsync(message, "delete vaulted card", ct);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal reporting allows at most a 31-day window per query; walk the whole range in chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var url = $"{_baseUrl}/v1/reporting/transactions" +
                          $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                          $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                          $"&fields=all&page_size={ReportingPageSize}&page={page}";
                // Reporting occasionally answers 5xx; retry transient failures (GET is safe to retry).
                var response = await SendWithRetryAsync<TransactionSearchResponse>(
                    () => new HttpRequestMessage(HttpMethod.Get, url), "search transactions", ct);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null)
                        {
                            continue;
                        }
                        results.Add(new PayPalTransaction(
                            info.TransactionId,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            ParseMoney(info.TransactionAmount, 0m),
                            info.TransactionAmount?.CurrencyCode ?? _settings.Currency,
                            info.FeeAmount is null ? null : ParseMoney(info.FeeAmount, 0m),
                            info.TransactionInitiationDate,
                            info.CustomField,
                            info.InvoiceId,
                            info.PaypalReferenceId));
                    }
                }

                totalPages = response.TotalPages;
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == windowStart ? to : windowEnd;
        }

        return results;
    }

    // --- HTTP plumbing ---

    /// <summary>Send with a small backoff retry on transient (5xx/429) failures. The factory rebuilds the request each attempt.</summary>
    private async Task<T> SendWithRetryAsync<T>(Func<HttpRequestMessage> factory, string operation, CancellationToken ct, int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var message = factory();
            try
            {
                return await SendAsync<T>(message, operation, ct);
            }
            catch (PayPalApiException ex) when (attempt < maxAttempts && (ex.HttpStatus >= 500 || ex.HttpStatus == 429))
            {
                _logger.LogWarning("Transient PayPal failure on {0} (HTTP {1}); retrying (attempt {2}/{3}).",
                    operation, ex.HttpStatus, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, string operation, CancellationToken ct)
    {
        var response = await SendRawAsync(message, operation, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new PayPalApiException($"PayPal returned an empty body for {operation}.", (int)response.StatusCode);
        }
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new PayPalApiException($"PayPal returned an unreadable body for {operation}.", (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new PayPalApiException($"Could not parse PayPal's response for {operation}: {ex.Message}", (int)response.StatusCode);
        }
    }

    private async Task SendAsync(HttpRequestMessage message, string operation, CancellationToken ct)
    {
        var response = await SendRawAsync(message, operation, ct);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage message, string operation, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, operation, ct);
        }
        return response;
    }

    private async Task ThrowForErrorAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        string? issue = null;
        string? debugId = null;
        string message = $"PayPal request to {operation} failed with HTTP {status}.";

        var body = await response.Content.ReadAsStringAsync(ct);
        response.Dispose();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
                if (error is not null)
                {
                    issue = error.Details is { Count: > 0 } ? error.Details[0].Issue : error.Name;
                    debugId = error.DebugId;
                    var description = error.Details is { Count: > 0 } ? error.Details[0].Description : error.Message;
                    message = $"PayPal {operation} failed ({status}): {issue} — {description} (debug_id: {debugId}).";
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; keep the generic message (never echo card data).
            }
        }

        _logger.LogWarning("PayPal {0} failed: HTTP {1}, issue {2}, debug_id {3}.", operation, status, issue ?? "(none)", debugId ?? "(none)");
        throw new PayPalApiException(message, status, issue, debugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            message.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new PayPalApiException(
                    $"Could not obtain a PayPal access token (HTTP {status}). Check PayPal:ClientId/ClientSecret.", status);
            }

            var token = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(JsonOptions, ct)
                ?? throw new PayPalApiException("PayPal returned an unreadable token response.", (int)response.StatusCode);
            response.Dispose();

            _cachedToken = token.AccessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // --- helpers ---

    private static CardBillingAddressModel? ToBillingAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new CardBillingAddressModel
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static AuthorizationModel? FindAuthorization(CreateOrderResponse order)
    {
        if (order.PurchaseUnits is null)
        {
            return null;
        }
        foreach (var unit in order.PurchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Count: > 0 })
            {
                return authorizations[0];
            }
        }
        return null;
    }

    private static bool HasPayerActionLink(List<LinkModel>? links)
    {
        if (links is null)
        {
            return false;
        }
        foreach (var link in links)
        {
            if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money? money, decimal fallback)
    {
        if (money?.Value is null || !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }
        return value;
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "-0000";
}
