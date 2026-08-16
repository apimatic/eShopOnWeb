using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPayPalPaymentGateway"/> over the AsadAli.Checkout.Sdk
/// (<c>PayPalServerSdkClient</c>). Direct-card / server-to-server only: no browser approval step.
/// Card numbers and security codes are never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string RepresentationPreference = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        CardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = BuildExpiry(card.ExpiryYear, card.ExpiryMonth),
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = BuildAddress(card.BillingAddress),
            },
        };

        return await AuthorizeAsync(amount, currency, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string vaultId, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest { VaultId = vaultId },
        };

        return await AuthorizeAsync(amount, currency, paymentSource, requestId, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string currency,
        PaymentSource paymentSource, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new[]
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = FormatAmount(amount),
                        },
                    },
                },
                PaymentSource = paymentSource,
            };

            Order order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: RepresentationPreference,
                ct: cancellationToken);

            string orderId = order.Id ?? string.Empty;

            // STOP on any challenge/approval signal — this integration cannot complete a browser step.
            ThrowIfApprovalRequired(order.Status, order.Links, order.PaymentSource?.Card);

            var auth = FirstAuthorization(order.PurchaseUnits);
            CardResponse? card = order.PaymentSource?.Card;

            // The authorization may not be present inline on the create-order response; if not, authorize explicitly.
            if (auth is null)
            {
                OrderAuthorizeResponse authorized = await _client.Orders.AuthorizeOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: RepresentationPreference,
                    ct: cancellationToken);

                ThrowIfApprovalRequired(authorized.Status, authorized.Links, authorized.PaymentSource?.Card);

                auth = FirstAuthorization(authorized.PurchaseUnits);
                card = authorized.PaymentSource?.Card ?? card;
            }

            if (auth is null)
            {
                throw new PaymentGatewayException(
                    "PayPal accepted the order but returned no authorization to act on.");
            }

            return new PayPalAuthorizationResult(
                PayPalOrderId: orderId,
                AuthorizationId: auth.Id ?? string.Empty,
                Status: auth.Status?.Value ?? string.Empty,
                ExpiresAt: ParseDate(auth.ExpirationTime),
                InstrumentDescription: DescribeInstrument(card));
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CapturedPayment captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null, // null body = full capture
                prefer: RepresentationPreference,
                ct: cancellationToken);

            SellerReceivableBreakdown? breakdown = captured.SellerReceivableBreakdown;
            decimal gross = ParseMoney(breakdown?.GrossAmount);
            decimal fee = breakdown?.PaypalFee is { } feeMoney ? ParseMoney(feeMoney) : 0m;
            decimal net = breakdown?.NetAmount is { } netMoney ? ParseMoney(netMoney) : gross;
            string currency = breakdown?.GrossAmount?.CurrencyCode
                              ?? captured.Amount?.CurrencyCode
                              ?? string.Empty;

            return new PayPalCaptureResult(
                CaptureId: captured.Id ?? string.Empty,
                Status: captured.Status?.Value ?? string.Empty,
                GrossAmount: gross,
                PayPalFee: fee,
                NetAmount: net,
                CurrencyCode: currency);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken = default)
    {
        try
        {
            PaymentAuthorization reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                },
                ct: cancellationToken);

            return new PayPalAuthorizationResult(
                PayPalOrderId: authorizationId,
                AuthorizationId: reauthorized.Id ?? string.Empty,
                Status: reauthorized.Status?.Value ?? string.Empty,
                ExpiresAt: ParseDate(reauthorized.ExpirationTime),
                InstrumentDescription: null);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            // Surfaced as PaymentGatewayException so the calling service can convert a
            // no-longer-reauthorizable failure into an operator-facing message.
            throw TranslateException(ex);
        }
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            PaymentAuthorization auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: cancellationToken);

            return new PayPalAuthorizationResult(
                PayPalOrderId: authorizationId,
                AuthorizationId: authorizationId,
                Status: auth.Status?.Value ?? string.Empty,
                ExpiresAt: ParseDate(auth.ExpirationTime),
                InstrumentDescription: null);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                ct: cancellationToken);
        }
        catch (JsonException)
        {
            // A successful void returns 204 No Content; the SDK throws while trying to materialize an
            // empty PaymentAuthorization body. An empty-body void IS the success signal, so swallow it and
            // return normally. This swallow is scoped to the void call ONLY — a real void failure
            // (404/409/422) arrives as SdkException<VoidPaymentError> with a body, which is not a
            // JsonException and so still surfaces via the ShouldTranslate catch below.
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            Refund refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: requestId, // idempotency key
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                },
                ct: cancellationToken);

            return new PayPalRefundResult(
                RefundId: refund.Id ?? string.Empty,
                Status: refund.Status?.Value ?? string.Empty,
                Amount: ParseMoney(refund.Amount),
                CurrencyCode: refund.Amount?.CurrencyCode ?? currency);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(CardDetails card,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = card.Number,
                        Expiry = BuildExpiry(card.ExpiryYear, card.ExpiryMonth),
                        SecurityCode = card.SecurityCode,
                        Name = card.CardholderName,
                        BillingAddress = BuildAddress(card.BillingAddress),
                    },
                },
            };

            PaymentTokenResponse token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                ct: cancellationToken);

            CardPaymentTokenEntity? vaulted = token.PaymentSource?.Card;
            var (expiryMonth, expiryYear) = SplitExpiry(vaulted?.Expiry);

            return new PayPalVaultResult(
                VaultId: token.Id ?? string.Empty,
                CardBrand: vaulted?.Brand?.Value ?? "CARD",
                Last4: vaulted?.LastDigits ?? string.Empty,
                ExpiryMonth: expiryMonth,
                ExpiryYear: expiryYear);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        try
        {
            var transactions = new List<PayPalTransaction>();

            // PayPal's Transaction Search caps EACH request at a 31-day window, so walk [from, to] in
            // consecutive windows of at most 31 days and page through every window in full. Advancing the
            // next window's start to the previous window's end leaves no gaps; the final window ends
            // exactly at `to`. An empty/inverted range (from >= to) yields an empty list, not an error.
            var windowSize = TimeSpan.FromDays(31);
            var windowStart = from;

            while (windowStart < to)
            {
                var windowEnd = windowStart + windowSize;
                if (windowEnd > to)
                {
                    windowEnd = to;
                }

                string startDate = FormatSearchDate(windowStart);
                string endDate = FormatSearchDate(windowEnd);

                int page = 1;
                int totalPages;

                do
                {
                    SearchResponse response = await _client.TransactionSearch.SearchTransactions(
                        startDate: startDate,
                        endDate: endDate,
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        fields: "transaction_info",
                        pageSize: 100,
                        page: page,
                        ct: cancellationToken);

                    totalPages = response.TotalPages ?? 1;

                    if (response.TransactionDetails is { } details)
                    {
                        foreach (var detail in details)
                        {
                            if (detail.TransactionInfo is not { } info)
                            {
                                continue;
                            }

                            transactions.Add(new PayPalTransaction(
                                TransactionId: info.TransactionId ?? string.Empty,
                                Amount: ParseMoney(info.TransactionAmount),
                                CurrencyCode: info.TransactionAmount?.CurrencyCode ?? string.Empty,
                                Status: info.TransactionStatus ?? string.Empty,
                                Date: ParseDate(info.TransactionInitiationDate) ?? default));
                        }
                    }

                    page++;
                }
                while (page <= totalPages);

                windowStart = windowEnd;
            }

            return transactions;
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw TranslateException(ex);
        }
    }

    // ----- helpers -------------------------------------------------------------------------------

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    // PayPal reporting accepts an RFC 3339 timestamp with an offset, e.g. 2024-01-01T00:00:00-0000.
    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string BuildExpiry(string expiryYear, string expiryMonth) =>
        $"{expiryYear}-{expiryMonth.PadLeft(2, '0')}"; // card expiry wire format is YYYY-MM

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode ?? string.Empty,
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static string? DescribeInstrument(CardResponse? card)
    {
        if (card is null)
        {
            return null;
        }

        string brand = card.Brand?.Value ?? "CARD";
        return $"{brand} ****{card.LastDigits}";
    }

    private static (string? Month, string? Year) SplitExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return (null, null);
        }

        string[] parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value is not { } value)
        {
            return 0m;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static void ThrowIfApprovalRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links,
        CardResponse? card)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires shopper approval (PAYER_ACTION_REQUIRED); this server-to-server flow cannot continue.");
        }

        if (links is not null && links.Any(l =>
                l.Rel is { } rel &&
                (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ||
                 rel.Equals("approve", StringComparison.OrdinalIgnoreCase))))
        {
            throw new PaymentApprovalRequiredException(
                "PayPal returned a payer-action link (approval/3DS challenge); this server-to-server flow cannot continue.");
        }

        LiabilityShiftIndicator? liabilityShift = card?.AuthenticationResult?.LiabilityShift;
        if (liabilityShift == LiabilityShiftIndicator.Possible || liabilityShift == LiabilityShiftIndicator.Unknown)
        {
            throw new PaymentApprovalRequiredException(
                "PayPal signalled a 3DS liability shift; this server-to-server flow cannot complete the challenge.");
        }
    }

    /// <summary>
    /// Only translate the failure kinds we know how to map; a caller-initiated cancellation and our own
    /// domain exceptions (approval-required, already-translated gateway errors) propagate untouched.
    /// </summary>
    private static bool ShouldTranslate(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false; // genuine caller cancellation — let it flow
        }

        return ex is JsonException
            or HttpRequestException
            or TaskCanceledException
            or SdkException<CreateOrderError>
            or SdkException<AuthorizeOrderError>
            or SdkException<CaptureAuthorizedPaymentError>
            or SdkException<ReauthorizePaymentError>
            or SdkException<GetAuthorizedPaymentError>
            or SdkException<VoidPaymentError>
            or SdkException<RefundCapturedPaymentError>
            or SdkException<CreatePaymentTokenError>
            or SdkException<RawError>;
    }

    private static PaymentGatewayException TranslateException(Exception ex) => ex switch
    {
        SdkException<CreateOrderError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<AuthorizeOrderError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<CaptureAuthorizedPaymentError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<ReauthorizePaymentError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<GetAuthorizedPaymentError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<VoidPaymentError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<RefundCapturedPaymentError> e =>
            FromError(e.Error.TryGetError(out var v) ? v : null, e.Error, e),
        SdkException<CreatePaymentTokenError> e =>
            FromError1(e.Error.TryGetError1(out var v) ? v : null, e.Error, e),
        SdkException<RawError> e => FromRaw(e.Error, e),

        // A JsonException reaches here from two opposite directions — an unreadable 2xx body (outcome
        // unknown) and a non-2xx body that did not match its generated error shape (a rejection whose
        // status was destroyed as the error object was built). We cannot tell them apart at this boundary
        // and the HTTP status is not recoverable, so we deliberately do NOT assert a 5xx: statusCode stays
        // null (unknown) rather than telling a retrying caller a deterministic rejection is an outage.
        JsonException => new PaymentGatewayException(
            "PayPal returned a response that could not be processed.",
            statusCode: null, payPalErrorName: null, debugId: null, inner: ex),

        // Transport failures (HttpRequestException / SDK per-attempt timeout as TaskCanceledException).
        _ => new PaymentGatewayException(
            "PayPal could not be reached.",
            statusCode: null, payPalErrorName: null, debugId: null, inner: ex),
    };

    private static PaymentGatewayException FromError(Error? error, ApiError apiError, Exception inner) =>
        new PaymentGatewayException(
            error?.Message ?? "PayPal rejected the request.",
            StatusOf(apiError),
            error?.Name,
            error?.DebugId,
            inner);

    private static PaymentGatewayException FromError1(Error1? error, ApiError apiError, Exception inner) =>
        new PaymentGatewayException(
            error?.Message ?? "PayPal rejected the request.",
            StatusOf(apiError),
            error?.Name,
            error?.DebugId,
            inner);

    private static PaymentGatewayException FromRaw(RawError raw, Exception inner)
    {
        int status = (int)raw.StatusCode;
        try
        {
            if (raw.ReadAsJson<DefaultError>() is { } body)
            {
                return new PaymentGatewayException(body.Message, status, body.Name, body.DebugId, inner);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — fall through to a caller-safe generic message.
        }

        return new PaymentGatewayException($"PayPal returned HTTP {status}.", status,
            payPalErrorName: null, debugId: null, inner: inner);
    }

    // TryGetRawError is the only place a numeric HTTP status lives on a typed (Case A) error, and it is not
    // populated when a more-specific typed body accessor already matched — so status may be null here.
    private static int? StatusOf(ApiError apiError) =>
        apiError.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;
}
