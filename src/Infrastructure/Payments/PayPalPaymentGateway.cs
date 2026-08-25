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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Talks to PayPal (direct card processing + vault) via the PayPalServerSdk. All PayPal-specific
/// concerns (wire shapes, error payloads, idempotency headers) are translated here; nothing above this
/// class (ApplicationCore, PublicApi) knows the PayPal SDK exists.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(string requestId, decimal amount, string currencyCode, CardDetails card, CancellationToken ct = default)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest { Amount = BuildAmount(amount, currencyCode) }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(card) }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return await EnsureAuthorizedAsync(order.Id!, order.Status, order.PurchaseUnits, requestId, ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrder(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while authorizing the payment.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while authorizing the payment.", ex, isRetryable: true);
        }
    }

    public async Task<PaymentAuthorizationResult> AuthorizeWithVaultedCardAsync(string requestId, decimal amount, string currencyCode, string vaultId, CancellationToken ct = default)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest { Amount = BuildAmount(amount, currencyCode) }
            },
            PaymentSource = new PaymentSource
            {
                Token = new Token { Id = vaultId, Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN") }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return await EnsureAuthorizedAsync(order.Id!, order.Status, order.PurchaseUnits, requestId, ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrder(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while authorizing the payment.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while authorizing the payment.", ex, isRetryable: true);
        }
    }

    /// <summary>
    /// A direct-card CreateOrder with intent=AUTHORIZE may or may not complete the authorization inline -
    /// this isn't settled by the SDK's contract, so we check for a nested authorization first and only
    /// call AuthorizeOrder as a fallback (see the PayPal integration plan for the grounding).
    /// </summary>
    private async Task<PaymentAuthorizationResult> EnsureAuthorizedAsync(string payPalOrderId, PayPalOrderStatus? status, IReadOnlyList<PurchaseUnit>? purchaseUnits, string requestId, CancellationToken ct)
    {
        if (status == PayPalOrderStatus.PayerActionRequired)
        {
            return new PaymentAuthorizationResult(payPalOrderId, string.Empty, status.Value, null, true);
        }

        var authorization = purchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization is null)
        {
            OrderAuthorizeResponse authorizeResponse;
            try
            {
                authorizeResponse = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateAuthorizeOrder(ex.Error);
            }

            if (authorizeResponse.Status == PayPalOrderStatus.PayerActionRequired)
            {
                return new PaymentAuthorizationResult(payPalOrderId, string.Empty, authorizeResponse.Status.Value, null, true);
            }

            authorization = authorizeResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException("PayPal did not return a payment authorization for this order.");
        }

        return new PaymentAuthorizationResult(
            payPalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseDate(authorization.ExpirationTime),
            false);
    }

    public async Task<PaymentCaptureResult> CapturePaymentAsync(string requestId, string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            return new PaymentCaptureResult(
                capture.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
                capture.Status?.Value ?? "UNKNOWN",
                ParseRequiredAmount(capture.Amount?.Value, "a captured amount"),
                ParseOptionalAmount(breakdown?.PaypalFee?.Value),
                ParseOptionalAmount(breakdown?.NetAmount?.Value),
                capture.Amount?.CurrencyCode ?? string.Empty);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCapture(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while capturing the payment.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while capturing the payment.", ex, isRetryable: true);
        }
    }

    public async Task<PaymentAuthorizationResult> ReauthorizePaymentAsync(string requestId, string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest { Amount = BuildMoney(amount, currencyCode) };

        try
        {
            var result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new PaymentAuthorizationResult(
                string.Empty,
                result.Id ?? throw new PaymentGatewayException("PayPal did not return an authorization id when reauthorizing."),
                result.Status?.Value ?? "UNKNOWN",
                ParseDate(result.ExpirationTime),
                false);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorize(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while reauthorizing the payment.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while reauthorizing the payment.", ex, isRetryable: true);
        }
    }

    public async Task VoidPaymentAsync(string requestId, string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateVoid(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while voiding the authorization.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while voiding the authorization.", ex, isRetryable: true);
        }
    }

    public async Task<PaymentRefundResult> RefundCaptureAsync(string requestId, string captureId, decimal? amount, string? currencyCode, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = BuildMoney(amount.Value, currencyCode ?? string.Empty) }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new PaymentRefundResult(
                refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
                refund.Status?.Value ?? "UNKNOWN",
                ParseRequiredAmount(refund.Amount?.Value, "a refund amount"),
                refund.Amount?.CurrencyCode ?? currencyCode ?? string.Empty);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefund(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while refunding the payment.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while refunding the payment.", ex, isRetryable: true);
        }
    }

    public async Task<VaultedCardResult> VaultCardAsync(string requestId, string merchantCustomerId, CardDetails card, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.ExpiryYearMonth,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(payPalRequestId: requestId, body: body, ct: ct);

            var cardEntity = token.PaymentSource?.Card;
            return new VaultedCardResult(
                token.Id ?? throw new PaymentGatewayException("PayPal did not return a vault id."),
                cardEntity?.Brand?.Value ?? "UNKNOWN",
                cardEntity?.LastDigits ?? string.Empty,
                cardEntity?.Expiry ?? card.ExpiryYearMonth);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateCreatePaymentToken(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while saving the card.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while saving the card.", ex, isRetryable: true);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateDeletePaymentToken(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while deleting the card.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("Could not reach PayPal while deleting the card.", ex, isRetryable: true);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var page = 1;
        while (true)
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex.Error, "searching transactions");
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while searching transactions.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PaymentGatewayException("Could not reach PayPal while searching transactions.", ex, isRetryable: true);
            }

            var details = response.TransactionDetails;
            if (details is null || details.Count == 0)
            {
                break;
            }

            foreach (var detail in details)
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null) continue;

                results.Add(new PayPalTransactionRecord(
                    info.TransactionId,
                    ParseOptionalAmount(info.TransactionAmount?.Value),
                    info.TransactionAmount?.CurrencyCode,
                    info.TransactionStatus,
                    ParseDate(info.TransactionInitiationDate),
                    ParseDate(info.TransactionUpdatedDate)));
            }

            var totalPages = response.TotalPages ?? page;
            if (page >= totalPages)
            {
                break;
            }

            page++;
        }

        return results;
    }

    // ---- request builders ----

    private static AmountWithBreakdown BuildAmount(decimal amount, string currencyCode) =>
        new AmountWithBreakdown { CurrencyCode = currencyCode, Value = FormatAmount(amount) };

    private static Money BuildMoney(decimal amount, string currencyCode) =>
        new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) };

    private static CardRequest BuildCardRequest(CardDetails card) => new CardRequest
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = card.ExpiryYearMonth,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card)
    };

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea1 = card.State,
            AdminArea2 = card.City,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseRequiredAmount(string? value, string what)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new PaymentGatewayException($"PayPal response did not include {what}.");
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseOptionalAmount(string? value) =>
        string.IsNullOrEmpty(value) ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value) ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    // ---- error translation (Case A: typed {Operation}Error; Case B: RawError directly) ----

    private static PaymentGatewayException TranslateCreateOrder(CreateOrderError error)
    {
        if (error.TryGetError(out var err)) return BuildFrom(err);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "creating the order");
        return new PaymentGatewayException("PayPal rejected the order creation request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateAuthorizeOrder(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var err)) return BuildFrom(err);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "authorizing the order");
        return new PaymentGatewayException("PayPal rejected the order authorization request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateCapture(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var err)) return BuildFrom(err);
        if (error.TryGetNoContent(out var noContent)) return BuildFrom(noContent, "capturing the payment", isRetryable: true);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "capturing the payment");
        return new PaymentGatewayException("PayPal rejected the capture request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateReauthorize(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var err))
        {
            // PayPal signals "authorization can no longer be reauthorized" as a 422 with free-form
            // Name/Details text (not a documented enum value) - treat any typed 422 here as non-retryable
            // so the caller surfaces it to the operator instead of retrying it as if it were transient.
            return BuildFrom(err, isRetryable: false);
        }
        if (error.TryGetNoContent(out var noContent)) return BuildFrom(noContent, "reauthorizing the payment", isRetryable: true);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "reauthorizing the payment");
        return new PaymentGatewayException("PayPal rejected the reauthorization request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateVoid(VoidPaymentError error)
    {
        if (error.TryGetError(out var err)) return BuildFrom(err);
        if (error.TryGetNoContent(out var noContent)) return BuildFrom(noContent, "voiding the authorization", isRetryable: true);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "voiding the authorization");
        return new PaymentGatewayException("PayPal rejected the void request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateRefund(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var err)) return BuildFrom(err);
        if (error.TryGetNoContent(out var noContent)) return BuildFrom(noContent, "refunding the payment", isRetryable: true);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "refunding the payment");
        return new PaymentGatewayException("PayPal rejected the refund request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateCreatePaymentToken(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var err)) return BuildFrom(err);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "saving the card");
        return new PaymentGatewayException("PayPal rejected the card vaulting request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateDeletePaymentToken(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var err)) return BuildFrom(err);
        if (error.TryGetRawError(out var raw)) return BuildFrom(raw, "deleting the card");
        return new PaymentGatewayException("PayPal rejected the card deletion request for an unrecognised reason.");
    }

    private static PaymentGatewayException TranslateRaw(RawError raw, string action) => BuildFrom(raw, action);

    private static PaymentGatewayException BuildFrom(Error err, bool isRetryable = false)
    {
        var details = err.Details?.Select(d => $"{d.Issue}: {d.Description}").ToList();
        return new PaymentGatewayException(DescribeError(err.Name, err.Message, details), errorCode: err.Name, details: details, isRetryable: isRetryable);
    }

    private static PaymentGatewayException BuildFrom(Error1 err, bool isRetryable = false)
    {
        var details = err.Details?.Select(d => $"{d.Issue}: {d.Description}").ToList();
        return new PaymentGatewayException(DescribeError(err.Name, err.Message, details), errorCode: err.Name, details: details, isRetryable: isRetryable);
    }

    private static PaymentGatewayException BuildFrom(RawError raw, string action, bool isRetryable = false)
    {
        string body;
        try { body = raw.ReadAsString(); } catch { body = string.Empty; }
        var retryable = isRetryable || (int)raw.StatusCode >= 500;
        return new PaymentGatewayException($"PayPal request failed with HTTP {(int)raw.StatusCode} while {action}. {body}", errorCode: raw.StatusCode.ToString(), isRetryable: retryable);
    }

    private static string DescribeError(string name, string message, IReadOnlyList<string>? details)
    {
        var detailText = details is null || details.Count == 0 ? string.Empty : " " + string.Join("; ", details);
        return $"PayPal rejected the request ({name}): {message}.{detailText}";
    }
}
