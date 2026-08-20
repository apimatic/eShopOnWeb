using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalHttpGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalHttpGateway> _logger;

    public PayPalHttpGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalHttpGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
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

    public async Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        AuthorizePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var createBody = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnit>
            {
                new()
                {
                    InvoiceId = request.InvoiceId,
                    CustomId = request.InvoiceId,
                    Amount = new PayPalMoneyDto
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    }
                }
            },
            PaymentSource = new PayPalPaymentSource
            {
                Card = BuildCardSource(request)
            }
        };

        var created = await SendJsonAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "v2/checkout/orders",
            createBody,
            request.RequestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.Equals(created.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step that this integration does not support.");
        }

        var authorization = ExtractAuthorization(created);
        if (authorization == null && !string.IsNullOrEmpty(created.Id))
        {
            var authorized = await SendJsonAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"v2/checkout/orders/{created.Id}/authorize",
                new { },
                request.RequestId + "-authorize",
                cancellationToken,
                preferRepresentation: true);

            if (string.Equals(authorized.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException(
                    "PayPal required a shopper approval step that this integration does not support.");
            }

            authorization = ExtractAuthorization(authorized);
            created.Id = authorized.Id ?? created.Id;
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException(502, "PayPal did not return an authorization for the order.");
        }

        return ToAuthorizationResult(created.Id ?? string.Empty, authorization);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendJsonAsync<PayPalAuthorizationResource>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = resource.Id ?? authorizationId,
            Status = resource.Status ?? string.Empty,
            ExpirationTime = ParseTime(resource.ExpirationTime),
            CreateTime = ParseTime(resource.CreateTime)
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalAmountRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = currency,
                Value = FormatAmount(amount)
            }
        };

        var resource = await SendJsonAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrEmpty(resource.Id))
        {
            throw new PaymentException(502, "PayPal did not return a renewed authorization.");
        }

        return ToAuthorizationResult(string.Empty, resource);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalAmountRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = currency,
                Value = FormatAmount(amount)
            },
            FinalCapture = true
        };

        var capture = await SendJsonAsync<PayPalCaptureResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (capture.SellerReceivableBreakdown == null && !string.IsNullOrEmpty(capture.Id))
        {
            capture = await SendJsonAsync<PayPalCaptureResource>(
                HttpMethod.Get,
                $"v2/payments/captures/{capture.Id}",
                body: null,
                requestId: null,
                cancellationToken);
        }

        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentException(502, "PayPal did not return a capture id.");
        }

        var capturedAmount = ParseAmount(capture.Amount?.Value)
            ?? ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value)
            ?? amount;
        var fee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee?.Value) ?? 0m;
        var reportedNet = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value);
        var derivedNet = capturedAmount - fee;
        var net = reportedNet is decimal paypalNet && Math.Abs(paypalNet - derivedNet) <= 0.05m
            ? paypalNet
            : derivedNet;

        return new PayPalCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status ?? string.Empty,
            CapturedAmount = capturedAmount,
            PayPalFee = fee,
            NetAmount = net
        };
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            new { },
            requestId,
            cancellationToken,
            preferRepresentation: true,
            allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        string currency,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalAmountRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = currency,
                Value = FormatAmount(amount)
            }
        };

        var refund = await SendJsonAsync<PayPalRefundResource>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new PaymentException(502, "PayPal did not return a refund id.");
        }

        return new PayPalRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status ?? string.Empty,
            Amount = ParseAmount(refund.Amount?.Value) ?? amount
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
        string? payPalCustomerId,
        CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            Customer = string.IsNullOrEmpty(payPalCustomerId) ? null : new PayPalCustomer { Id = payPalCustomerId },
            PaymentSource = new PayPalPaymentSource
            {
                Card = BuildCardSource(new AuthorizePaymentRequest
                {
                    InvoiceId = "vault",
                    Currency = Currency,
                    Amount = 0,
                    RequestId = "vault",
                    Card = card
                })
            }
        };

        var setup = await SendJsonAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupRequest,
            Guid.NewGuid().ToString(),
            cancellationToken);

        if (string.Equals(setup.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step that this integration does not support.");
        }

        if (string.IsNullOrEmpty(setup.Id))
        {
            throw new PaymentException(502, "PayPal did not return a setup token.");
        }

        var tokenRequest = new PayPalCreatePaymentTokenRequest
        {
            PaymentSource = new PayPalTokenSource
            {
                Token = new PayPalTokenRef { Id = setup.Id, Type = "SETUP_TOKEN" }
            }
        };

        var token = await SendJsonAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenRequest,
            Guid.NewGuid().ToString(),
            cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException(502, "PayPal did not return a payment token.");
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = token.Id,
            CustomerId = token.Customer?.Id ?? setup.Customer?.Id,
            LastDigits = token.PaymentSource?.Card?.LastDigits ?? LastDigits(card.Number),
            Brand = token.PaymentSource?.Card?.Brand ?? string.Empty,
            Expiry = token.PaymentSource?.Card?.Expiry ?? card.Expiry,
            CardholderName = token.PaymentSource?.Card?.Name ?? card.Name
        };
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            if (windowEnd == windowStart)
            {
                await FetchTransactionPages(windowStart, windowEnd, results, cancellationToken);
                break;
            }

            // PayPal's maximum supported range is 31 days; keep each window within that bound.
            var inclusiveEnd = windowEnd;
            if (inclusiveEnd - windowStart > TimeSpan.FromDays(31))
            {
                inclusiveEnd = windowStart.AddDays(31);
            }

            await FetchTransactionPages(windowStart, inclusiveEnd, results, cancellationToken);
            if (inclusiveEnd >= to)
            {
                break;
            }

            windowStart = inclusiveEnd;
        }

        return results;
    }

    private async Task FetchTransactionPages(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var start = FormatPayPalDate(from);
        var end = FormatPayPalDate(to);
        var page = 1;
        int totalPages;
        do
        {
            var path =
                $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=all&balance_affecting_records_only=N";

            var response = await SendJsonAsync<PayPalTransactionSearchResponse>(
                HttpMethod.Get,
                path,
                body: null,
                requestId: null,
                cancellationToken,
                allowNotFoundAsEmpty: true);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        PayPalReferenceId = info.PayPalReferenceId,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        InitiationDate = info.TransactionInitiationDate,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Amount = ParseAmount(info.TransactionAmount?.Value),
                        FeeAmount = ParseAmount(info.FeeAmount?.Value)
                    });
                }
            }

            totalPages = response.TotalPages <= 0 ? 1 : response.TotalPages;
            page++;
        } while (page <= totalPages);
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        bool allowEmpty = false,
        bool allowNotFoundAsEmpty = false) where T : class, new()
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var client = _httpClientFactory.CreateClient("PayPal");
            using var httpRequest = new HttpRequestMessage(method, path);
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
            {
                httpRequest.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (preferRepresentation)
            {
                httpRequest.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, SerializerOptions);
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode == 409 && attempt < maxAttempts &&
                responseBody.Contains("PREVIOUS_REQUEST_IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (allowNotFoundAsEmpty && ((int)response.StatusCode == 404 || responseBody.Contains("Data for the given start date is not available", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("PayPal reporting returned no data for {Path} (status {Status}).", path, (int)response.StatusCode);
                    return new T();
                }
                throw MapError(path, (int)response.StatusCode, responseBody);
            }

            if (allowEmpty && string.IsNullOrWhiteSpace(responseBody))
            {
                return new T();
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                throw new PaymentException(502, $"PayPal returned an empty response for {method} {path}.");
            }

            var parsed = JsonSerializer.Deserialize<T>(responseBody, SerializerOptions);
            return parsed ?? new T();
        }

        throw new PaymentException(409, "PayPal reported a previous request still in progress.");
    }

    private PaymentException MapError(string path, int statusCode, string responseBody)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(responseBody, SerializerOptions);
        }
        catch (JsonException)
        {
            // Fall through with a generic message; never include the raw PayPal body in case it echoes card data.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description ?? error?.Message;
        var name = error?.Name;
        _logger.LogWarning(
            "PayPal request to {Path} failed with status {Status}. Name {Name}. Issue {Issue}. DebugId {DebugId}.",
            path,
            statusCode,
            name,
            issue,
            error?.DebugId);

        var message = string.IsNullOrWhiteSpace(description)
            ? $"PayPal request failed ({statusCode})."
            : description;

        if (!string.IsNullOrEmpty(issue))
        {
            message = $"{issue}: {message}";
        }

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PayerActionRequiredException(
                "PayPal required a shopper approval step that this integration does not support.");
        }

        return new PaymentException(statusCode >= 400 ? statusCode : 502, message);
    }

    private static PayPalCardSource BuildCardSource(AuthorizePaymentRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            return new PayPalCardSource { VaultId = request.VaultId };
        }

        var card = request.Card ?? throw new PaymentException(400, "Card details are required.");
        return new PayPalCardSource
        {
            Number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalAuthorizationResource? ExtractAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static PayPalAuthorizationResult ToAuthorizationResult(string payPalOrderId, PayPalAuthorizationResource authorization) =>
        new()
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id ?? string.Empty,
            AuthorizationStatus = authorization.Status ?? string.Empty,
            ExpirationTime = ParseTime(authorization.ExpirationTime),
            CreateTime = ParseTime(authorization.CreateTime),
            Amount = ParseAmount(authorization.Amount?.Value)
        };

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string LastDigits(string number)
    {
        var digits = number.Replace(" ", string.Empty, StringComparison.Ordinal);
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
