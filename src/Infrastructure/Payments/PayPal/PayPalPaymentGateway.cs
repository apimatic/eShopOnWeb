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
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private const string PreferRepresentation = "return=representation";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        var payPalOrder = await CreateOrderAsync(orderId, amount, currency, instanceKey, cancellationToken);
        EnsureNoPayerAction(payPalOrder);

        var authorizeRequest = new PayPalAuthorizeRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Card = ToCardRequest(card)
            }
        };

        var authorized = await AuthorizeOrderAsync(payPalOrder.Id!, authorizeRequest, $"eshop-pay-{orderId}-{instanceKey}", cancellationToken);
        return ToAuthorizationResult(authorized);
    }

    public async Task<PaymentAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        var payPalOrder = await CreateOrderAsync(orderId, amount, currency, instanceKey, cancellationToken);
        EnsureNoPayerAction(payPalOrder);

        var authorizeRequest = new PayPalAuthorizeRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Card = new PayPalCardRequest
                {
                    VaultId = vaultId,
                    StoredCredential = new PayPalStoredCredential
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME",
                        Usage = "SUBSEQUENT"
                    }
                }
            }
        };

        var authorized = await AuthorizeOrderAsync(payPalOrder.Id!, authorizeRequest, $"eshop-pay-{orderId}-{instanceKey}", cancellationToken);
        return ToAuthorizationResult(authorized);
    }

    public async Task<PaymentAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var auth = await SendAsync<PayPalAuthorization>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return new PaymentAuthorizationDetails
        {
            AuthorizationId = auth.Id ?? authorizationId,
            Status = auth.Status ?? string.Empty,
            Created = ParseTimestamp(auth.CreateTime),
            Expiration = ParseTimestamp(auth.ExpirationTime)
        };
    }

    public async Task<PaymentAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new PayPalReauthorizeRequest
        {
            Amount = Money(amount, currency)
        };

        var auth = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            request,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(auth.Id))
        {
            throw new PaymentGatewayException("PayPal reauthorization did not return an authorization id.");
        }

        return new PaymentAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = auth.Id,
            Status = auth.Status ?? "CREATED",
            Created = ParseTimestamp(auth.CreateTime),
            Expiration = ParseTimestamp(auth.ExpirationTime),
            Amount = auth.Amount?.Value
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            idempotencyKey,
            cancellationToken,
            preferRepresentation: false);
    }

    public async Task<PaymentCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            request,
            idempotencyKey,
            cancellationToken);

        return ToCaptureResult(capture);
    }

    public async Task<PaymentCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToCaptureResult(capture);
    }

    public async Task<PaymentRefundResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new PayPalRefundRequest
        {
            Amount = Money(amount, currency)
        };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            request,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refund.Id))
        {
            throw new PaymentGatewayException("PayPal refund did not return a refund id.");
        }

        return new PaymentRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status ?? "COMPLETED",
            Amount = ParseAmount(refund.Amount) ?? amount
        };
    }

    public async Task<SavedCardResult> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        var request = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalCustomer
            {
                Id = IsPayPalCustomerId(payPalCustomerId) ? payPalCustomerId : null,
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = ToCardRequest(card, includeVerification: false)
            }
        };

        var response = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            request,
            $"eshop-vault-{merchantCustomerId}-{Guid.NewGuid():N}",
            cancellationToken);

        EnsureNoPayerAction(response.Status, response.Links);

        if (string.IsNullOrWhiteSpace(response.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a vault payment-token id.");
        }

        var cardResponse = response.PaymentSource?.Card;
        return new SavedCardResult
        {
            VaultId = response.Id,
            PayPalCustomerId = response.Customer?.Id,
            LastDigits = cardResponse?.LastDigits,
            Brand = cardResponse?.Brand,
            Expiry = cardResponse?.Expiry,
            Name = cardResponse?.Name
        };
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            cancellationToken,
            preferRepresentation: false);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<GatewayTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            if (windowEnd <= windowStart)
            {
                windowEnd = windowStart.AddSeconds(1);
            }

            await AddWindowAsync(results, windowStart, windowEnd, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task AddWindowAsync(
        List<GatewayTransaction> results,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = FormatPayPalDate(from);
            var end = FormatPayPalDate(to);
            var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page={page}&page_size=500&fields=transaction_info";
            var response = await SendAsync<PayPalSearchResponse>(
                HttpMethod.Get,
                path,
                body: null,
                requestId: null,
                cancellationToken,
                preferRepresentation: false);

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        ReferenceIdType = info.PaypalReferenceIdType,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = info.TransactionAmount?.Value,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        FeeAmount = info.FeeAmount?.Value,
                        InitiationDate = ParseTimestamp(info.TransactionInitiationDate),
                        UpdatedDate = ParseTimestamp(info.TransactionUpdatedDate)
                    });
                }
            }

            totalPages = response.TotalPages ?? page;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalOrderResponse> CreateOrderAsync(
        int orderId,
        decimal amount,
        string currency,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    CustomId = $"{orderId}-{instanceKey}",
                    InvoiceId = $"ESHOP-{orderId}-{instanceKey}",
                    Description = $"eShopOnWeb order {orderId}",
                    Amount = Money(amount, currency)
                }
            }
        };

        var created = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            request,
            $"eshop-create-{orderId}-{instanceKey}",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(created.Id))
        {
            throw new PaymentGatewayException("PayPal did not return an order id.");
        }

        return created;
    }

    private async Task<PayPalOrderResponse> AuthorizeOrderAsync(
        string payPalOrderId,
        PayPalAuthorizeRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var authorized = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{payPalOrderId}/authorize",
            request,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(authorized);
        return authorized;
    }

    private static PaymentAuthorizationResult ToAuthorizationResult(PayPalOrderResponse order)
    {
        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PaymentGatewayException("PayPal authorization did not return an authorization id. The hold was not placed.");
        }

        return new PaymentAuthorizationResult
        {
            PayPalOrderId = order.Id ?? string.Empty,
            AuthorizationId = authorization.Id,
            Status = authorization.Status ?? order.Status ?? "CREATED",
            Created = ParseTimestamp(authorization.CreateTime),
            Expiration = ParseTimestamp(authorization.ExpirationTime),
            Amount = authorization.Amount?.Value
        };
    }

    private static PaymentCaptureResult ToCaptureResult(PayPalCapture capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Id))
        {
            throw new PaymentGatewayException("PayPal capture did not return a capture id.");
        }

        var capturedAmount = ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount)
            ?? ParseAmount(capture.Amount)
            ?? 0m;

        return new PaymentCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status ?? "COMPLETED",
            CapturedAmount = capturedAmount,
            PayPalFee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee),
            NetAmount = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount)
        };
    }

    private static PayPalCardRequest ToCardRequest(CardPaymentDetails card, bool includeVerification = true)
    {
        var request = new PayPalCardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };

        if (includeVerification)
        {
            request.Attributes = new PayPalCardAttributes
            {
                Verification = new PayPalCardVerification { Method = "SCA_WHEN_REQUIRED" }
            };
        }

        return request;
    }

    private static void EnsureNoPayerAction(PayPalOrderResponse order) =>
        EnsureNoPayerAction(order.Status, order.Links);

    private static void EnsureNoPayerAction(string? status, IEnumerable<PayPalLink>? links)
    {
        if (!string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new PaymentChallengeRequiredException(
            "PayPal required a shopper approval challenge (for example 3-D Secure) that cannot be completed without a browser. Direct card processing stopped as required.");
    }

    private static bool IsPayPalCustomerId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 22;

    private static PayPalMoney Money(decimal amount, string currency) =>
        new()
        {
            CurrencyCode = currency,
            Value = FormatAmount(amount, currency)
        };

    internal static string FormatAmount(decimal amount, string currency)
    {
        var decimals = IsZeroDecimal(currency) ? 0 : 2;
        return decimal.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString(decimals == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
    }

    private static bool IsZeroDecimal(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("HUF", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("KRW", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("TWD", StringComparison.OrdinalIgnoreCase);

    private static decimal? ParseAmount(PayPalMoney? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
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

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = true)
    {
        var response = await SendAsync(method, path, body, requestId, cancellationToken, preferRepresentation);
        if (response is null)
        {
            throw new PaymentGatewayException($"PayPal {method} {path} returned an empty body.");
        }

        var parsed = JsonSerializer.Deserialize<T>(response, PayPalJson.Options);
        if (parsed is null)
        {
            throw new PaymentGatewayException($"PayPal {method} {path} returned a body that could not be read.");
        }

        return parsed;
    }

    private async Task<string?> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation)
    {
        var options = _options.Value;
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("PayPal");
        var url = $"{options.ResolveBaseUrl().TrimEnd('/')}{path}";

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", PreferRepresentation);
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, PayPalJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var debugId = TryReadDebugId(responseBody);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("{PayPalCall}", CardDataRedactor.DescribeWithoutSecrets(method.Method, path, (int)response.StatusCode, debugId));
            return string.IsNullOrWhiteSpace(responseBody) ? null : responseBody;
        }

        var error = TryReadError(responseBody);
        var message = BuildErrorMessage(error, (int)response.StatusCode);
        _logger.LogWarning("{PayPalCall} {Error}", CardDataRedactor.DescribeWithoutSecrets(method.Method, path, (int)response.StatusCode, error?.DebugId ?? debugId), CardDataRedactor.Redact(message));

        throw new PaymentGatewayException(message, (int)response.StatusCode, error?.DebugId ?? debugId);
    }

    private static PayPalError? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PayPalError>(body, PayPalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadDebugId(string body) => TryReadError(body)?.DebugId;

    private static string BuildErrorMessage(PayPalError? error, int statusCode)
    {
        if (error is null)
        {
            return $"PayPal request failed with HTTP {statusCode}.";
        }

        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}".Trim()))
            : null;

        var parts = new[] { error.Name, error.Message, details }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var combined = string.Join(" — ", parts);
        return string.IsNullOrWhiteSpace(combined)
            ? $"PayPal request failed with HTTP {statusCode}."
            : CardDataRedactor.Redact(combined);
    }
}
