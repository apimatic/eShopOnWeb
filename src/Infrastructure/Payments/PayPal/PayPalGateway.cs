using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalGateway : IPayPalGateway
{
    private const string PreferRepresentation = "return=representation";
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayPalOrderAuthorization> AuthorizeCardPaymentAsync(
        PayPalCardAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                CreatePurchaseUnit(request.Amount, request.Currency, request.CustomId, request.InvoiceId)
            },
            PaymentSource = new PayPalPaymentSource
            {
                Card = new PayPalCardRequest
                {
                    Name = request.CardholderName,
                    Number = request.CardNumber,
                    Expiry = request.Expiry,
                    SecurityCode = request.SecurityCode,
                    BillingAddress = MapAddress(request.BillingAddress)
                }
            }
        };

        return await CreateAuthorizedOrderAsync(orderRequest, request.RequestId, cancellationToken);
    }

    public async Task<PayPalOrderAuthorization> AuthorizeVaultedCardPaymentAsync(
        PayPalVaultAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                CreatePurchaseUnit(request.Amount, request.Currency, request.CustomId, request.InvoiceId)
            },
            PaymentSource = new PayPalPaymentSource
            {
                Card = new PayPalCardRequest
                {
                    VaultId = request.VaultId,
                    StoredCredential = new PayPalStoredCredential
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            }
        };

        return await CreateAuthorizedOrderAsync(orderRequest, request.RequestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest
        {
            Amount = Money(amount, currency)
        };

        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken);

        return MapAuthorization(authorization);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            cancellationToken,
            allowEmptyBody: true);
    }

    public async Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);

        return MapCapture(capture);
    }

    public async Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalRefundRequest
        {
            Amount = Money(amount, currency)
        };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refund.Id) || string.IsNullOrWhiteSpace(refund.Status))
        {
            throw new PayPalGatewayException("PayPal refund response did not include an id and status.");
        }

        return new PayPalRefundDetails
        {
            RefundId = refund.Id,
            Status = refund.Status,
            Amount = ParseAmount(refund.Amount?.Value) ?? amount,
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalVaultCardRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalCustomer { MerchantCustomerId = SanitizeCustomerId(request.MerchantCustomerId) },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = new PayPalCardRequest
                {
                    Name = request.CardholderName,
                    Number = request.CardNumber,
                    Expiry = request.Expiry,
                    SecurityCode = request.SecurityCode,
                    BillingAddress = MapAddress(request.BillingAddress)
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            request.RequestId,
            cancellationToken);

        EnsureNoPayerActionRequired(token.Status, token.Links, "saving a card");

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new PayPalGatewayException("PayPal vault response did not include a payment token id.");
        }

        var card = token.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            VaultId = token.Id,
            LastDigits = card?.LastDigits,
            Brand = card?.Brand,
            Expiry = card?.Expiry,
            CardholderName = card?.Name ?? request.CardholderName
        };
    }

    public async Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{vaultId}",
                body: null,
                requestId: null,
                cancellationToken,
                allowEmptyBody: true);
        }
        catch (PayPalGatewayException ex) when (ex.HttpStatus == 404)
        {
            _logger.LogInformation("PayPal vault token {VaultId} was already deleted.", vaultId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("Reconciliation 'to' must be on or after 'from'.");
        }

        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart + MaxTransactionSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await FetchTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            if (windowEnd == to)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task FetchTransactionWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> sink,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = FormatPayPalDate(from);
            var end = FormatPayPalDate(to);
            var path =
                $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(
                HttpMethod.Get,
                path,
                body: null,
                requestId: null,
                cancellationToken);

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null || string.IsNullOrWhiteSpace(info.TransactionId))
                    {
                        continue;
                    }

                    sink.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        Amount = ParseAmount(info.TransactionAmount?.Value),
                        FeeAmount = ParseAmount(info.FeeAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        InitiationDate = ParseTimestamp(info.TransactionInitiationDate)
                    });
                }
            }

            totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalOrderAuthorization> CreateAuthorizedOrderAsync(
        PayPalOrderRequest orderRequest,
        string requestId,
        CancellationToken cancellationToken)
    {
        var order = await SendAsync<PayPalOrder>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            orderRequest,
            requestId,
            cancellationToken);

        EnsureNoPayerActionRequired(order.Status, order.Links, "paying for an order");

        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PayPalGatewayException(
                "PayPal authorized the request but did not return an authorization id. The payment cannot be captured later.");
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException("PayPal denied the card authorization.");
        }

        var details = MapAuthorization(authorization);
        return new PayPalOrderAuthorization
        {
            PayPalOrderId = order.Id ?? string.Empty,
            PayPalOrderStatus = order.Status ?? string.Empty,
            AuthorizationId = details.AuthorizationId,
            AuthorizationStatus = details.Status,
            Amount = details.Amount,
            Currency = details.Currency,
            CreatedAt = details.CreatedAt,
            ExpiresAt = details.ExpiresAt
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmptyBody = false)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("PayPal");
        var url = $"{_options.ResolveBaseUrl()}{path}";

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", PreferRepresentation);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, PayPalJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        if ((int)response.StatusCode == 204 || (allowEmptyBody && string.IsNullOrWhiteSpace(responseBody) && response.IsSuccessStatusCode))
        {
            return default!;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException((int)response.StatusCode, responseBody, debugId);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        var parsed = JsonSerializer.Deserialize<T>(responseBody, PayPalJson.Options);
        if (parsed is null)
        {
            throw new PayPalGatewayException("PayPal returned an empty or unreadable JSON body.", (int)response.StatusCode, debugId: debugId);
        }

        return parsed;
    }

    private static PayPalGatewayException CreateApiException(int status, string responseBody, string? debugId)
    {
        PayPalError? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalError>(responseBody, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Body is not the documented error model; fall through with a generic message.
        }

        var detail = error?.Details?.FirstOrDefault();
        var message = error?.Message
            ?? "PayPal request failed.";
        if (!string.IsNullOrWhiteSpace(detail?.Description))
        {
            message = $"{message} {detail!.Description}";
        }
        else if (!string.IsNullOrWhiteSpace(detail?.Issue))
        {
            message = $"{message} Issue: {detail!.Issue}";
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            message = $"{message} Debug id: {debugId}";
        }

        return new PayPalGatewayException(message, status, error?.Name, debugId);
    }

    private static void EnsureNoPayerActionRequired(string? status, IEnumerable<PayPalLink>? links, string action)
    {
        var payerAction = links?.FirstOrDefault(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || payerAction is not null)
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal required a shopper to approve {action} in a browser (status {status}). Direct card processing cannot continue without that round-trip.");
        }
    }

    private static PayPalPurchaseUnitRequest CreatePurchaseUnit(decimal amount, string currency, string customId, string invoiceId)
    {
        return new PayPalPurchaseUnitRequest
        {
            Amount = Money(amount, currency),
            CustomId = customId,
            InvoiceId = invoiceId,
            Description = $"eShopOnWeb order {customId}"
        };
    }

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = FormatAmount(amount)
    };

    private static PayPalAddress MapAddress(PayPalBillingAddress address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.AdminArea2,
        AdminArea1 = address.AdminArea1,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static PayPalAuthorizationDetails MapAuthorization(PayPalAuthorization authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization.Id) || string.IsNullOrWhiteSpace(authorization.Status))
        {
            throw new PayPalGatewayException("PayPal authorization response was missing id or status.");
        }

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = authorization.Id,
            Status = authorization.Status,
            Amount = ParseAmount(authorization.Amount?.Value) ?? 0m,
            Currency = authorization.Amount?.CurrencyCode ?? string.Empty,
            CreatedAt = ParseTimestamp(authorization.CreateTime),
            ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
        };
    }

    private static PayPalCaptureDetails MapCapture(PayPalCapture capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Id) || string.IsNullOrWhiteSpace(capture.Status))
        {
            throw new PayPalGatewayException("PayPal capture response was missing id or status.");
        }

        var currency = capture.Amount?.CurrencyCode
            ?? capture.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode
            ?? string.Empty;

        return new PayPalCaptureDetails
        {
            CaptureId = capture.Id,
            Status = capture.Status,
            CapturedAmount = ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value)
                ?? ParseAmount(capture.Amount?.Value)
                ?? 0m,
            PaypalFee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee?.Value),
            NetAmount = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value),
            Currency = currency
        };
    }

    internal static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatPayPalDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string SanitizeCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#').ToArray());
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrEmpty(sanitized) ? "eshop-shopper" : sanitized;
    }

    private static string RedactPath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }
}
