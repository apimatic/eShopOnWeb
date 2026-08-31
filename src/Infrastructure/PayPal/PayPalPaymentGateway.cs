using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of IPaymentGateway over the REST APIs:
/// Orders v2 (authorize with card or vaulted card), Payments v2 (capture, void,
/// reauthorize, refund), Vault v3 (save/delete cards), Reporting v1 (transactions).
/// Full card details transit through here to PayPal only; they are never logged
/// and never stored. Logs carry request ids and PayPal debug ids only.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const int MaxReportingWindowDays = 31; // PayPal caps a transaction search at 31 days
    private const int ReportingPageSize = 500;     // PayPal's maximum page size

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PaymentGatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var card = request.VaultPaymentTokenId is not null
            ? new PayPalCard { VaultId = request.VaultPaymentTokenId }
            : MapCard(request.Card!);

        var body = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = request.ReferenceId,
                    CustomId = request.ReferenceId,
                    InvoiceId = request.InvoiceId,
                    Amount = Money(request.Amount, request.Currency)
                }
            },
            PaymentSource = new PayPalPaymentSource { Card = card }
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey, preferRepresentation: true, cancellationToken);

        if (order.Status == "PAYER_ACTION_REQUIRED")
        {
            // The integration is server-to-server; a browser challenge cannot be completed here.
            throw new PaymentGatewayException(
                "PayPal requires a browser-based buyer approval (PAYER_ACTION_REQUIRED) for this card, " +
                "which this server-to-server integration does not support.",
                issue: "PAYER_ACTION_REQUIRED", isDecline: true);
        }

        var authorization = order.PurchaseUnits?.Count > 0
            ? order.PurchaseUnits[0].Payments?.Authorizations?.Count > 0
                ? order.PurchaseUnits[0].Payments!.Authorizations![0]
                : null
            : null;
        if (authorization is null)
        {
            throw new PaymentGatewayException(
                $"PayPal order {order.Id} returned status {order.Status} without an authorization.",
                issue: order.Status);
        }

        return new PaymentGatewayAuthorization
        {
            AuthorizationId = authorization.Id,
            GatewayOrderId = order.Id,
            Status = authorization.Status,
            Amount = ParseMoney(authorization.Amount),
            Currency = authorization.Amount?.CurrencyCode ?? request.Currency,
            ExpiresAt = ParseDate(authorization.ExpirationTime),
            CardBrand = order.PaymentSource?.Card?.Brand,
            CardLast4 = order.PaymentSource?.Card?.LastDigits
        };
    }

    public async Task<PaymentGatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);
        return MapAuthorization(auth);
    }

    public async Task<PaymentGatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, idempotencyKey, preferRepresentation: true, cancellationToken);
        return MapAuthorization(auth);
    }

    public async Task<PaymentGatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            InvoiceId = invoiceId,
            FinalCapture = true
        };
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, preferRepresentation: true, cancellationToken);

        return new PaymentGatewayCapture
        {
            CaptureId = capture.Id,
            Status = capture.Status,
            Amount = ParseMoney(capture.Amount),
            Currency = capture.Amount?.CurrencyCode ?? currency,
            Fee = capture.SellerReceivableBreakdown?.PayPalFee is null ? null : ParseMoney(capture.SellerReceivableBreakdown.PayPalFee),
            NetAmount = capture.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(capture.SellerReceivableBreakdown.NetAmount),
            CapturedAt = ParseDate(capture.CreateTime)
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            body: null, idempotencyKey, preferRepresentation: false, cancellationToken);
    }

    public async Task<PaymentGatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? invoiceId, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currency) : null, // omitted amount = full refund
            InvoiceId = invoiceId,
            NoteToPayer = noteToPayer,
            CustomId = invoiceId
        };
        var refund = await SendAsync<PayPalRefundResponse>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, preferRepresentation: true, cancellationToken);

        return new PaymentGatewayRefund
        {
            RefundId = refund.Id,
            Status = refund.Status,
            Amount = ParseMoney(refund.Amount),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<VaultedCardResult> VaultCardAsync(GatewayCardDetails card, string? gatewayCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupBody = new PayPalSetupTokenRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = MapCard(card) },
            Customer = gatewayCustomerId is null ? null : new PayPalCustomer { Id = gatewayCustomerId }
        };
        var setupToken = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post, "/v3/vault/setup-tokens",
            setupBody, idempotencyKey, preferRepresentation: false, cancellationToken);

        if (setupToken.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PaymentGatewayException(
                "PayPal requires a browser-based buyer approval (PAYER_ACTION_REQUIRED) to save this card, " +
                "which this server-to-server integration does not support.",
                issue: "PAYER_ACTION_REQUIRED", isDecline: true);
        }

        var paymentTokenBody = new PayPalCreatePaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Token = new PayPalTokenSource { Id = setupToken.Id, Type = "SETUP_TOKEN" }
            }
        };
        var paymentToken = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            paymentTokenBody, idempotencyKey + "-pt", preferRepresentation: false, cancellationToken);

        var vaultedCard = paymentToken.PaymentSource?.Card ?? setupToken.PaymentSource?.Card;
        return new VaultedCardResult
        {
            PaymentTokenId = paymentToken.Id,
            CustomerId = paymentToken.Customer?.Id ?? setupToken.Customer?.Id ?? string.Empty,
            CardBrand = vaultedCard?.Brand ?? "Card",
            Last4 = vaultedCard?.LastDigits ?? "????",
            Expiry = vaultedCard?.Expiry,
            CardholderName = vaultedCard?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = new List<GatewayTransaction>();

        // PayPal caps a search window at 31 days; chunk longer ranges so the whole range is covered.
        for (var windowStart = from; windowStart < to; windowStart = windowStart.AddDays(MaxReportingWindowDays))
        {
            var windowEnd = windowStart.AddDays(MaxReportingWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            // Page through the whole window, not just the first page.
            for (var page = 1; ; page++)
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                    "&fields=transaction_info" +
                    $"&page_size={ReportingPageSize}&page={page}";

                var result = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path,
                    body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);

                if (result.TransactionDetails is not null)
                {
                    foreach (var detail in result.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }
                        transactions.Add(new GatewayTransaction
                        {
                            TransactionId = info.TransactionId ?? string.Empty,
                            ReferenceId = info.PayPalReferenceId,
                            ReferenceIdType = info.PayPalReferenceIdType,
                            EventCode = info.TransactionEventCode,
                            InitiationDate = ParseDate(info.TransactionInitiationDate),
                            UpdatedDate = ParseDate(info.TransactionUpdatedDate),
                            Amount = info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount),
                            Currency = info.TransactionAmount?.CurrencyCode,
                            Fee = info.FeeAmount is null ? null : ParseMoney(info.FeeAmount),
                            Status = info.TransactionStatus
                        });
                    }
                }

                if (page >= result.TotalPages || result.TransactionDetails is null || result.TransactionDetails.Count == 0)
                {
                    break;
                }
            }
        }

        return transactions;
    }

    private static PaymentGatewayAuthorization MapAuthorization(PayPalAuthorization auth) => new()
    {
        AuthorizationId = auth.Id,
        Status = auth.Status,
        Amount = ParseMoney(auth.Amount),
        Currency = auth.Amount?.CurrencyCode ?? string.Empty,
        ExpiresAt = ParseDate(auth.ExpirationTime)
    };

    private static PayPalCard MapCard(GatewayCardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = new PayPalAddress
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode
        }
    };

    private static PayPalAmount Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalAmount? amount) =>
        amount is null ? 0m : decimal.Parse(amount.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatReportDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

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

            _settings.Validate();
            var baseUrl = _settings.ResolveBaseUrl();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with {StatusCode}", (int)response.StatusCode);
                throw new PaymentGatewayException(
                    $"PayPal rejected the client credentials (HTTP {(int)response.StatusCode}).",
                    gatewayStatusCode: (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthResponse>(payload);
            _accessToken = token?.AccessToken ?? throw new PaymentGatewayException("PayPal returned no access token.");
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token!.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey,
        bool preferRepresentation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, idempotencyKey, preferRepresentation, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrEmpty(payload))
        {
            throw new PaymentGatewayException($"PayPal returned an empty response for {method} {path}.");
        }
        return JsonSerializer.Deserialize<T>(payload)
            ?? throw new PaymentGatewayException($"Could not parse PayPal's response for {method} {path}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey,
        bool preferRepresentation, CancellationToken cancellationToken)
    {
        var baseUrl = _settings.ResolveBaseUrl();
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            // Bodies may carry full card details: they go to PayPal over TLS and are never logged.
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("PayPal {Method} {Path} -> {StatusCode} (request id {RequestId})",
                method, path, (int)response.StatusCode, idempotencyKey ?? "-");
            return response;
        }

        var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(errorPayload);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic exception below.
        }

        var issue = error?.Details?.Count > 0 ? error.Details[0].Issue : error?.Name;
        var description = error?.Details?.Count > 0 ? error.Details[0].Description : error?.Message;
        _logger.LogWarning("PayPal {Method} {Path} failed: {StatusCode} {Issue} (debug id {DebugId}, request id {RequestId})",
            method, path, (int)response.StatusCode, issue ?? "unknown", error?.DebugId ?? "-", idempotencyKey ?? "-");

        response.Dispose();
        throw new PaymentGatewayException(
            $"PayPal {method} {path} failed: {issue ?? "HTTP " + (int)response.StatusCode} {description}".Trim(),
            issue: issue,
            debugId: error?.DebugId,
            gatewayStatusCode: (int)response.StatusCode,
            isDecline: (int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
    }
}
