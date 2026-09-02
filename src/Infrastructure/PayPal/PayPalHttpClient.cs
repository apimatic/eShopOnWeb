using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTP client for the PayPal REST API. Card details flow through here to PayPal
/// and are never logged or persisted.
/// </summary>
public class PayPalHttpClient : IPayPalClient
{
    private const int MaxTransactionSearchPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // PayPal caches responses by PayPal-Request-Id for 6 hours. Prefixing with a
    // per-process id keeps ids stable within a run (idempotent retries) without
    // colliding with cached responses from a previous run of the app.
    private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalHttpClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalHttpClient(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalHttpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string customId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new PayPalPurchaseUnitRequest
                {
                    CustomId = customId,
                    // Merchant accounts can block duplicate invoice ids; keep it globally unique
                    // (custom_id stays the stable order reference for reconciliation).
                    InvoiceId = $"{customId}-{Guid.NewGuid():N}",
                    Amount = new PayPalMoney { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }
        };

        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);
        return response.Id ?? throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal create order response did not contain an order id.", null);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentSourceRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = MapCard(card) }
        };
        return await AuthorizeAsync(payPalOrderId, request, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(string payPalOrderId, string vaultTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentSourceRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = new PayPalCard { VaultId = vaultTokenId } }
        };
        return await AuthorizeAsync(payPalOrderId, request, requestId, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(string payPalOrderId, PayPalPaymentSourceRequest request, string requestId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", request, requestId, cancellationToken);
        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id == null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal authorize response did not contain an authorization.", null);
        }
        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAmountRequest
        {
            Amount = new PayPalMoney { CurrencyCode = currency, Value = FormatAmount(amount) }
        };
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, requestId, cancellationToken);
        return MapAuthorization(authorization);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", new { }, requestId, cancellationToken);
        if (capture.Id == null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal capture response did not contain a capture id.", null);
        }

        // The capture response can be minimal; fetch full details for the amounts and fee breakdown.
        if (capture.Amount == null || capture.SellerReceivableBreakdown == null)
        {
            return await GetCaptureAsync(capture.Id, cancellationToken);
        }

        return MapCapture(capture);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Get, $"/v2/payments/captures/{captureId}", null, null, cancellationToken);
        return MapCapture(capture);
    }

    private static PayPalCaptureResult MapCapture(PayPalCapture capture)
    {
        return new PayPalCaptureResult
        {
            CaptureId = capture.Id ?? string.Empty,
            Status = capture.Status ?? string.Empty,
            Amount = ParseAmount(capture.Amount?.Value),
            Currency = capture.Amount?.CurrencyCode ?? string.Empty,
            PayPalFee = capture.SellerReceivableBreakdown?.PayPalFee?.Value is string fee ? ParseAmount(fee) : null,
            NetAmount = capture.SellerReceivableBreakdown?.NetAmount?.Value is string net ? ParseAmount(net) : null
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", new { }, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new PayPalAmountRequest { Amount = new PayPalMoney { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : new { };

        var refund = await SendAsync<PayPalRefundResponse>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        if (refund.Id == null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal refund response did not contain a refund id.", null);
        }

        return new PayPalRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status ?? string.Empty,
            Amount = ParseAmount(refund.Amount?.Value),
            Currency = refund.Amount?.CurrencyCode ?? string.Empty
        };
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentSourceRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = MapCard(card) }
        };
        var response = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post, "/v3/vault/setup-tokens", request, requestId, cancellationToken);
        if (response.Id == null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal setup token response did not contain an id.", null);
        }

        return new PayPalSetupTokenResult
        {
            SetupTokenId = response.Id,
            Status = response.Status ?? string.Empty,
            CustomerId = response.Customer?.Id
        };
    }

    public async Task<PayPalVaultedCardResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentSourceRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Token = new PayPalTokenSource { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };
        var response = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);
        if (response.Id == null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal payment token response did not contain an id.", null);
        }

        return new PayPalVaultedCardResult
        {
            VaultTokenId = response.Id,
            CustomerId = response.Customer?.Id,
            Brand = response.PaymentSource?.Card?.Brand ?? string.Empty,
            LastFourDigits = response.PaymentSource?.Card?.LastDigits ?? string.Empty,
            Expiry = response.PaymentSource?.Card?.Expiry ?? string.Empty
        };
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var path = "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                $"&end_date={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                $"&fields=transaction_info&page_size={MaxTransactionSearchPageSize}&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null) continue;

                    transactions.Add(new PayPalTransaction
                    {
                        TransactionId = info.TransactionId,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Subject = info.TransactionSubject,
                        Amount = info.TransactionAmount?.Value is string value ? ParseAmount(value) : null,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Fee = info.FeeAmount?.Value is string fee ? ParseAmount(fee) : null,
                        InitiatedAt = ParseDate(info.TransactionInitiationDate),
                        UpdatedAt = ParseDate(info.TransactionUpdatedDate),
                        ReferenceId = info.PayPalReferenceId,
                        CustomField = info.CustomField
                    });
                }
            }

            totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
            page++;
        }

        return transactions;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return Activator.CreateInstance<T>();
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PayPalApiException(HttpStatusCode.InternalServerError, null, $"Could not parse PayPal response for {method} {path}.", null);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (!string.IsNullOrEmpty(requestId))
        {
            var scopedRequestId = $"{_instanceId}-{requestId}";
            if (scopedRequestId.Length > 108)
            {
                scopedRequestId = scopedRequestId.Substring(0, 108);
            }
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", scopedRequestId);
        }
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Error payloads contain no card data; safe to surface message + debug id.
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            PayPalErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(errorContent, JsonOptions); }
            catch (JsonException) { /* non-JSON error body */ }

            var detailText = error?.Details == null ? null : string.Join("; ", error.Details
                .Where(d => d.Issue != null || d.Description != null)
                .Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}"));

            _logger.LogWarning("PayPal {Method} {Path} failed with {StatusCode} ({ErrorName}, debug id {DebugId})",
                method, path, (int)response.StatusCode, error?.Name, error?.DebugId);

            var message = error?.Message ?? $"PayPal {method} {path} failed with status {(int)response.StatusCode}.";
            if (!string.IsNullOrEmpty(detailText))
            {
                message = $"{message} [{detailText}]";
            }

            throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId);
        }

        _logger.LogInformation("PayPal {Method} {Path} succeeded with {StatusCode}", method, path, (int)response.StatusCode);
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedAccessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
            {
                return _cachedAccessToken;
            }

            if (string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new PayPalApiException(HttpStatusCode.InternalServerError, null,
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (env vars PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET or user-secrets).", null);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(response.StatusCode, null,
                    $"PayPal token request failed with status {(int)response.StatusCode}. Check the configured credentials.", null);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken == null)
            {
                throw new PayPalApiException(HttpStatusCode.InternalServerError, null, "PayPal token response did not contain an access token.", null);
            }

            _cachedAccessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalCard MapCard(CardDetails card)
    {
        return new PayPalCard
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = new PayPalAddress
            {
                AddressLine1 = card.BillingAddressLine1,
                AddressLine2 = card.BillingAddressLine2,
                AdminArea1 = card.BillingState,
                AdminArea2 = card.BillingCity,
                PostalCode = card.BillingPostalCode,
                CountryCode = card.BillingCountryCode
            }
        };
    }

    private static PayPalAuthorizationResult MapAuthorization(PayPalAuthorization authorization)
    {
        return new PayPalAuthorizationResult
        {
            AuthorizationId = authorization.Id ?? string.Empty,
            Status = authorization.Status ?? string.Empty,
            Amount = ParseAmount(authorization.Amount?.Value),
            Currency = authorization.Amount?.CurrencyCode ?? string.Empty,
            ExpiresAt = ParseDate(authorization.ExpirationTime)
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
