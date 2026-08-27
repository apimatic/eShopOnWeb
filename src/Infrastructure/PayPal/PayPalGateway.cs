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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTP implementation of <see cref="IPayPalGateway"/> against PayPal's REST APIs.
/// Request and response bodies are never logged because they may contain card data;
/// only operation names, ids and status codes are written to logs.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const int TransactionSearchPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string referenceId,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = referenceId,
                    Amount = Money(amount, currency)
                }
            },
            PaymentSource = BuildPaymentSource(paymentSource)
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);
        var payerAction = FindPayerActionUrl(order.Links);
        if (payerAction is not null || string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException(payerAction);
        }

        return new PayPalOrderResult(order.Id ?? string.Empty, order.Status ?? string.Empty, null,
            ExtractAuthorization(order));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeOrderRequest
        {
            PaymentSource = BuildPaymentSource(paymentSource)
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", request, requestId, cancellationToken);

        var payerAction = FindPayerActionUrl(order.Links);
        if (payerAction is not null || string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException(payerAction);
        }

        var authorization = ExtractAuthorization(order);
        if (authorization is null)
        {
            throw new PayPalGatewayException(HttpStatusCode.OK, null,
                $"PayPal order {payPalOrderId} was authorized but the response contained no authorization.");
        }

        return authorization;
    }

    private static PayPalAuthorizationResult? ExtractAuthorization(PayPalOrderResponse order)
    {
        var authorization = order.PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorizationResponse>())
            .FirstOrDefault();
        if (authorization?.Id is null)
        {
            return null;
        }

        return new PayPalAuthorizationResult(
            order.Id ?? string.Empty,
            order.Status ?? string.Empty,
            authorization.Id,
            authorization.Status ?? string.Empty,
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime),
            null);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorizationResponse>(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);

        return new PayPalAuthorizationDetails(
            authorization.Id ?? authorizationId,
            authorization.Status ?? string.Empty,
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAmountRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCaptureResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request, requestId, cancellationToken);

        // The capture POST returns only id/status/links; the amounts and the fee breakdown
        // are read back with a follow-up GET.
        var details = await GetCaptureAsync(capture.Id ?? string.Empty, cancellationToken);
        return details with { Status = string.IsNullOrEmpty(capture.Status) ? details.Status : capture.Status };
    }

    private async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var capture = await SendAsync<PayPalCaptureResponse>(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);

        return new PayPalCaptureResult(
            capture.Id ?? captureId,
            capture.Status ?? string.Empty,
            ParseMoney(capture.Amount),
            capture.Amount?.CurrencyCode ?? string.Empty,
            capture.SellerReceivableBreakdown?.PayPalFee is { } fee ? ParseMoney(fee) : null,
            capture.SellerReceivableBreakdown?.NetAmount is { } net ? ParseMoney(net) : null);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAmountRequest { Amount = Money(amount, currency) };

        var authorization = await SendAsync<PayPalAuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", request, requestId, cancellationToken);

        if (authorization.Id is not null && (authorization.Amount is null || authorization.ExpirationTime is null))
        {
            // Read the replacement authorization back if the POST response was sparse.
            return await GetAuthorizationAsync(authorization.Id, cancellationToken);
        }

        return new PayPalAuthorizationDetails(
            authorization.Id ?? string.Empty,
            authorization.Status ?? string.Empty,
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAmountRequest { Amount = Money(amount, currency) };

        var refund = await SendAsync<PayPalRefundResponse>(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request, idempotencyKey, cancellationToken);

        // The refund POST returns only id/status/links; read the amount back with a GET.
        var details = await SendAsync<PayPalRefundResponse>(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refund.Id ?? string.Empty)}", null, null, cancellationToken);

        return new PayPalRefundResult(
            refund.Id ?? string.Empty,
            string.IsNullOrEmpty(details.Status) ? refund.Status ?? string.Empty : details.Status,
            details.Amount is not null ? ParseMoney(details.Amount) : amount,
            details.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalSetupTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    Name = card.CardholderName,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card),
                    VerificationMethod = "SCA_WHEN_REQUIRED",
                    // Required so the setup token is verified (APPROVED) and can be exchanged for a
                    // payment token. The URLs are never hit in this server-to-server flow; if PayPal
                    // ever answers with a browser challenge, PayPalPayerActionRequiredException is thrown.
                    ExperienceContext = new PayPalExperienceContextRequest
                    {
                        BrandName = "eShopOnWeb",
                        Locale = "en-US",
                        ReturnUrl = "https://localhost/vault/return",
                        CancelUrl = "https://localhost/vault/cancel"
                    }
                }
            }
        };

        var setupToken = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post, "/v3/vault/setup-tokens", request, requestId, cancellationToken);

        var approveUrl = setupToken.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href;
        if (string.Equals(setupToken.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) && approveUrl is not null)
        {
            throw new PayPalPayerActionRequiredException(approveUrl);
        }

        return new PayPalSetupTokenResult(setupToken.Id ?? string.Empty, setupToken.Status ?? string.Empty,
            setupToken.Customer?.Id, approveUrl);
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreatePaymentTokenRequest
        {
            PaymentSource = new PayPalTokenSourceRequest
            {
                Token = new PayPalTokenReference { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);

        string? expiryMonth = null;
        string? expiryYear = null;
        var expiry = token.PaymentSource?.Card?.Expiry;
        if (!string.IsNullOrEmpty(expiry) && expiry.Length == 7 && expiry[4] == '-')
        {
            expiryYear = expiry.Substring(0, 4);
            expiryMonth = expiry.Substring(5, 2);
        }

        return new PayPalPaymentTokenResult(
            token.Id ?? string.Empty,
            token.Customer?.Id,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits,
            expiryMonth,
            expiryYear,
            token.PaymentSource?.Card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var records = new List<PayPalTransactionRecord>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var path = "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatTimestamp(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatTimestamp(to))}" +
                $"&fields=all&page_size={TransactionSearchPageSize}&page={page}";

            var result = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
            totalPages = result.TotalPages <= 0 ? 1 : result.TotalPages;

            foreach (var detail in result.TransactionDetails ?? new List<PayPalTransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }

                records.Add(new PayPalTransactionRecord(
                    info.TransactionId,
                    info.TransactionEventCode,
                    info.TransactionStatus,
                    info.TransactionAmount is { } amount ? ParseMoney(amount) : null,
                    info.TransactionAmount?.CurrencyCode,
                    info.FeeAmount is { } fee ? ParseMoney(fee) : null,
                    ParseTimestamp(info.TransactionInitiationDate),
                    ParseTimestamp(info.TransactionUpdatedDate)));
            }

            page++;
        }

        return records;
    }

    private static PayPalPaymentSourceRequest BuildPaymentSource(PayPalPaymentSource paymentSource)
    {
        if (paymentSource.VaultTokenId is not null)
        {
            return new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    VaultId = paymentSource.VaultTokenId,
                    StoredCredential = new PayPalStoredCredentialRequest
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            };
        }

        var card = paymentSource.Card ?? throw new ArgumentException("A payment source needs card details or a vault token.", nameof(paymentSource));
        return new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                Name = card.CardholderName,
                SecurityCode = card.SecurityCode,
                BillingAddress = BuildAddress(card)
            }
        };
    }

    private static PayPalAddressRequest? BuildAddress(PayPalCardDetails card)
    {
        if (card.BillingAddressLine1 is null && card.BillingCity is null && card.BillingCountryCode is null)
        {
            return null;
        }

        return new PayPalAddressRequest
        {
            AddressLine1 = card.BillingAddressLine1,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode
        };
    }

    private static string? FindPayerActionUrl(IEnumerable<PayPalLink>? links) =>
        links?.FirstOrDefault(l => l.Rel is "payer-action" or "approve")?.Href;

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money) =>
        money?.Value is { } value
            ? decimal.Parse(value, CultureInfo.InvariantCulture)
            : 0m;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new PayPalGatewayException(response.StatusCode, null,
                $"PayPal {method} {path} returned {(int)response.StatusCode} with an empty body.");
        }

        return JsonSerializer.Deserialize<T>(content)
            ?? throw new PayPalGatewayException(response.StatusCode, null,
                $"PayPal {method} {path} returned a body that could not be read.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        PayPalGatewayGuard.EnsureConfigured(_settings);

        var token = await _tokenProvider.GetTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // Bodies of failed calls never contain card data (PayPal error payloads), so they are safe to parse.
        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("PayPal {Method} {Path} failed with status {StatusCode}", method, path, (int)response.StatusCode);

        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(errorContent);
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. gateway HTML); fall through to a generic exception below.
        }

        response.Dispose();
        var issues = error?.Details?.Where(d => d.Issue is not null).Select(d => d.Issue!).ToList();
        var message = error?.Message ?? $"PayPal {method} {path} failed with status {(int)response.StatusCode}.";
        if (issues is { Count: > 0 })
        {
            message += $" Issues: {string.Join(", ", issues)}.";
        }
        if (error?.DebugId is not null)
        {
            message += $" DebugId: {error.DebugId}.";
        }

        throw new PayPalGatewayException(
            error is null ? HttpStatusCode.InternalServerError : response.StatusCode,
            error?.Name,
            message,
            issues,
            error?.DebugId);
    }
}
