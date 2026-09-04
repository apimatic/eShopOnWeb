using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Helpers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Thin wrapper over the PayPal .NET SDK. Every SDK interaction flows through here; the
/// SDK types never leak past this boundary.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string ChallengeIssue = "PAYER_ACTION_REQUIRED";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PayPalCreateOrderResult> CreateOrderAsync(
        int orderId, decimal amount, string currency, PayPalCardDetails? card, string? vaultId, string requestId, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = orderId.ToString(CultureInfo.InvariantCulture),
                    CustomId = $"eshop-order-{orderId}",
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = MoneyFormatter.ToPayPalAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = BuildCardRequest(card, vaultId)
            }
        };

        try
        {
            using var budget = Budget(ct);
            var response = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            var status = response.Status?.Value ?? string.Empty;
            if (response.Status == OrderStatus.PayerActionRequired ||
                string.Equals(status, ChallengeIssue, StringComparison.OrdinalIgnoreCase))
            {
                throw new CardChallengeException("PayPal asked the shopper to approve this card payment in a browser; the payment was not authorized.");
            }

            // For a card payment source the card is processed when the order is created, so
            // the authorization already exists in the response and no separate authorize call
            // is needed (calling authorize again would fail with ORDER_ALREADY_AUTHORIZED).
            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            return new PayPalCreateOrderResult(
                response.Id ?? string.Empty,
                status,
                authorization?.Id,
                authorization?.Status?.Value,
                ParseDateTime(authorization?.ExpirationTime));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                if (IsCardChallenge(error.Details))
                {
                    throw new CardChallengeException("PayPal asked the shopper to approve this card payment in a browser; the payment was not authorized.");
                }
                throw ProviderError("create order", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PayPalApiException($"PayPal create order was rejected (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
            }

            throw new PayPalApiException("PayPal create order failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("create order");
        }
    }

    public async Task<PayPalAuthorizeResult> AuthorizeOrderAsync(
        string payPalOrderId, PayPalCardDetails? card, string? vaultId, string requestId, CancellationToken ct)
    {
        // A card payment source is processed when the order is created, so the order is
        // normally already approved and authorize must NOT re-supply the card (doing so
        // re-presents the card and can be refused). When the order was not approved with
        // the payment source, authorize fails and we retry once with the card re-supplied.
        try
        {
            return await AuthorizeCoreAsync(payPalOrderId, null, requestId, ct);
        }
        catch (PayPalApiException) when (card is not null || !string.IsNullOrWhiteSpace(vaultId))
        {
            return await AuthorizeCoreAsync(payPalOrderId, BuildAuthorizeBody(card, vaultId), requestId, ct);
        }
    }

    private async Task<PayPalAuthorizeResult> AuthorizeCoreAsync(string payPalOrderId, OrderAuthorizeRequest? body, string requestId, CancellationToken ct)
    {
        try
        {
            using var budget = Budget(ct);
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            var orderStatus = response.Status?.Value ?? string.Empty;
            if (response.Status == OrderStatus.PayerActionRequired ||
                string.Equals(orderStatus, ChallengeIssue, StringComparison.OrdinalIgnoreCase))
            {
                throw new CardChallengeException("PayPal asked the shopper to approve this card payment in a browser; the payment was not authorized.");
            }

            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization is null)
            {
                throw new PayPalApiException("PayPal did not return an authorization for this order.", 422);
            }

            var authStatus = authorization.Status?.Value ?? string.Empty;
            if (string.Equals(authStatus, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(authorization.StatusDetails?.Reason?.Value, "PENDING_REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                throw new CardChallengeException("PayPal put this card payment under review; the payment was not authorized.");
            }

            return new PayPalAuthorizeResult(
                response.Id ?? payPalOrderId,
                orderStatus,
                authorization.Id,
                authStatus,
                ParseDateTime(authorization.ExpirationTime));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                if (IsCardChallenge(error.Details))
                {
                    throw new CardChallengeException("PayPal asked the shopper to approve this card payment in a browser; the payment was not authorized.");
                }
                throw ProviderError("authorize order", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PayPalApiException($"PayPal authorize order was rejected (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
            }

            throw new PayPalApiException("PayPal authorize order failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("authorize order");
        }
    }

    private static OrderAuthorizeRequest? BuildAuthorizeBody(PayPalCardDetails? card, string? vaultId)
    {
        return new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = BuildCardRequest(card, vaultId)
            }
        };
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            using var budget = Budget(ct);
            var response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            var breakdown = response.SellerReceivableBreakdown;
            return new PayPalCaptureResult(
                response.Id ?? string.Empty,
                response.Status?.Value ?? string.Empty,
                MoneyFormatter.ParsePayPalAmount(breakdown?.GrossAmount?.Value),
                MoneyFormatter.ParsePayPalAmount(breakdown?.PaypalFee?.Value),
                MoneyFormatter.ParsePayPalAmount(breakdown?.NetAmount?.Value),
                breakdown?.GrossAmount?.CurrencyCode ?? response.Amount?.CurrencyCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw ProviderError("capture", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetNoContent(out var raw))
            {
                throw new PayPalApiException("PayPal capture failed.", 502);
            }

            if (ex.Error.TryGetRawError(out var rawFallback))
            {
                throw new PayPalApiException($"PayPal capture was rejected (HTTP {(int)rawFallback.StatusCode}).", (int)rawFallback.StatusCode);
            }

            throw new PayPalApiException("PayPal capture failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("capture");
        }
    }

    public async Task<PayPalAuthorizationActionResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            using var budget = Budget(ct);
            var response = await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            return new PayPalAuthorizationActionResult(
                response.Id ?? authorizationId,
                response.Status?.Value ?? string.Empty,
                ParseDateTime(response.ExpirationTime));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw ProviderError("void authorization", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetNoContent(out var raw))
            {
                throw new PayPalApiException("PayPal void failed.", 502);
            }

            if (ex.Error.TryGetRawError(out var rawFallback))
            {
                throw new PayPalApiException($"PayPal void was rejected (HTTP {(int)rawFallback.StatusCode}).", (int)rawFallback.StatusCode);
            }

            throw new PayPalApiException("PayPal void failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("void authorization");
        }
    }

    public async Task<PayPalAuthorizationActionResult> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            using var budget = Budget(ct);
            var response = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            return new PayPalAuthorizationActionResult(
                response.Id ?? authorizationId,
                response.Status?.Value ?? string.Empty,
                ParseDateTime(response.ExpirationTime));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw ProviderError("reauthorize authorization", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetNoContent(out var raw))
            {
                throw new PayPalApiException("PayPal reauthorize failed.", 502);
            }

            if (ex.Error.TryGetRawError(out var rawFallback))
            {
                throw new PayPalApiException($"PayPal reauthorize was rejected (HTTP {(int)rawFallback.StatusCode}).", (int)rawFallback.StatusCode);
            }

            throw new PayPalApiException("PayPal reauthorize failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("reauthorize authorization");
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken ct)
    {
        var body = amount.HasValue
            ? new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = MoneyFormatter.ToPayPalAmount(amount.Value)
                }
            }
            : null;

        try
        {
            using var budget = Budget(ct);
            var response = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: budget.Token);

            return new PayPalRefundResult(
                response.Id ?? string.Empty,
                response.Status?.Value ?? string.Empty,
                MoneyFormatter.ParsePayPalAmount(response.Amount?.Value),
                response.Amount?.CurrencyCode ?? currency);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw ProviderError("refund", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetNoContent(out var raw))
            {
                throw new PayPalApiException("PayPal refund failed.", 502);
            }

            if (ex.Error.TryGetRawError(out var rawFallback))
            {
                throw new PayPalApiException($"PayPal refund was rejected (HTTP {(int)rawFallback.StatusCode}).", (int)rawFallback.StatusCode);
            }

            throw new PayPalApiException("PayPal refund failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("refund");
        }
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(PayPalCardDetails card, string requestId, string merchantCustomerId, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer
            {
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            using var budget = Budget(ct);
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId,
                body: body,
                requestOptions: null,
                ct: budget.Token);

            var storedCard = response.PaymentSource?.Card;
            return new PayPalPaymentTokenResult(
                response.Id ?? string.Empty,
                storedCard?.Brand?.Value,
                storedCard?.LastDigits,
                storedCard?.Expiry);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw ProviderError1("vault card", 422, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PayPalApiException($"PayPal vault card was rejected (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
            }

            throw new PayPalApiException("PayPal vault card failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("vault card");
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct)
    {
        try
        {
            using var budget = Budget(ct);
            await _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                requestOptions: null,
                ct: budget.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw ProviderError1("delete vaulted card", 400, error.Name, error.Message, error.Details);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if ((int)raw.StatusCode == 404)
                {
                    return;
                }

                throw new PayPalApiException($"PayPal delete vaulted card was rejected (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
            }

            throw new PayPalApiException("PayPal delete vaulted card failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw TranslateTransportFailure("delete vaulted card");
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransactionRecord>();

        // A single PayPal search covers at most 31 days; walk the whole requested range
        // in 30-day windows so the report covers everything the caller asked for.
        foreach (var (start, end) in ChunkRange(from, to))
        {
            await foreach (var page in SearchPageAsync(start, end, ct))
            {
                results.AddRange(page);
            }
        }

        return results;
    }

    private async IAsyncEnumerable<IReadOnlyList<PayPalTransactionRecord>> SearchPageAsync(DateTimeOffset from, DateTimeOffset to, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var page = 1;

        while (true)
        {
            SearchResponse response;
            try
            {
                using var budget = Budget(ct);
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatDate(from),
                    endDate: FormatDate(to),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "all",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    requestOptions: null,
                    ct: budget.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalApiException($"PayPal transaction search was rejected (HTTP {(int)ex.Error.StatusCode}).", (int)ex.Error.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                throw TranslateTransportFailure("transaction search");
            }

            var pageRecords = new List<PayPalTransactionRecord>();
            foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                pageRecords.Add(new PayPalTransactionRecord(
                    info?.TransactionId,
                    info?.PaypalReferenceId,
                    info?.TransactionEventCode,
                    info?.TransactionStatus,
                    ParseDateTime(info?.TransactionInitiationDate),
                    MoneyFormatter.ParsePayPalAmount(info?.TransactionAmount?.Value),
                    MoneyFormatter.ParsePayPalAmount(info?.FeeAmount?.Value),
                    info?.TransactionAmount?.CurrencyCode,
                    detail.PayerInfo?.EmailAddress,
                    info?.CustomField));
            }

            yield return pageRecords;

            var totalPages = response.TotalPages ?? 1;
            if (page >= totalPages || pageRecords.Count == 0)
            {
                yield break;
            }

            page++;
        }
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> ChunkRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            yield return (from, to);
            yield break;
        }

        var window = TimeSpan.FromDays(30);
        var start = from;
        while (start < to)
        {
            var end = start + window;
            if (end > to)
            {
                end = to;
            }

            yield return (start, end);

            if (end >= to)
            {
                yield break;
            }

            start = end;
        }
    }

    private static CardRequest BuildCardRequest(PayPalCardDetails? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new CardRequest
            {
                VaultId = vaultId
            };
        }

        if (card is null)
        {
            throw new PayPalApiException("A card or saved payment method is required to pay.", 400);
        }

        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address BuildAddress(PayPalCardAddress? address)
    {
        if (address is null)
        {
            throw new PayPalApiException("A billing address is required for card payments.", 400);
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static bool IsCardChallenge(IEnumerable<ErrorDetails>? details)
    {
        return details?.Any(d => string.Equals(d.Issue, ChallengeIssue, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static PayPalApiException ProviderError(string operation, int status, string? name, string? message, IEnumerable<ErrorDetails>? details)
    {
        var issue = details?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Issue))?.Issue;
        var detail = issue ?? message;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = name;
        }

        return new PayPalApiException(
            string.IsNullOrWhiteSpace(detail)
                ? $"PayPal {operation} was rejected (HTTP {status})."
                : $"PayPal {operation} was rejected (HTTP {status}): {detail}",
            status);
    }

    private static PayPalApiException ProviderError1(string operation, int status, string? name, string? message, IEnumerable<ErrorDetails1>? details)
    {
        var issue = details?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Issue))?.Issue;
        var detail = issue ?? message;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = name;
        }

        return new PayPalApiException(
            string.IsNullOrWhiteSpace(detail)
                ? $"PayPal {operation} was rejected (HTTP {status})."
                : $"PayPal {operation} was rejected (HTTP {status}): {detail}",
            status);
    }

    private PayPalApiException TranslateTransportFailure(string operation)
    {
        _logger.LogWarning("PayPal {Operation} failed because the provider was unreachable.", operation);
        return new PayPalApiException($"PayPal {operation} could not be completed because the provider was unreachable.", 502);
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        return budget;
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}