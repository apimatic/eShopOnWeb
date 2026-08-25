using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _currency = settings.Value.Currency;
    }

    public async Task<PayPalAuthorizeResult> AuthorizeWithCardAsync(
        decimal amount, string currency, PayPalCardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var orderBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            }
        };

        Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderBody,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateOrderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        if (createdOrder.Status == OrderStatus.PayerActionRequired)
            throw new PayPalException("Card requires browser approval; contact merchant account support.", statusCode: 400);

        // When a card is passed directly, PayPal may authorize immediately during CreateOrder
        var earlyAuth = createdOrder.PurchaseUnits?[0]?.Payments?.Authorizations?[0];
        if (earlyAuth?.Id != null)
        {
            return new PayPalAuthorizeResult
            {
                PayPalOrderId = createdOrder.Id ?? earlyAuth.Id,
                AuthorizationId = earlyAuth.Id,
                AuthorizationStatus = earlyAuth.Status?.Value ?? "CREATED"
            };
        }

        return await AuthorizeOrderAsync(createdOrder.Id!, $"{idempotencyKey}-auth", ct);
    }

    public async Task<PayPalAuthorizeResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string paymentTokenId, string idempotencyKey, CancellationToken ct = default)
    {
        var orderBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = paymentTokenId
                }
            }
        };

        Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderBody,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateOrderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        if (createdOrder.Status == OrderStatus.PayerActionRequired)
            throw new PayPalException("Payment method requires browser approval; contact merchant account support.", statusCode: 400);

        // Vault tokens may also complete authorization during CreateOrder
        var earlyAuth = createdOrder.PurchaseUnits?[0]?.Payments?.Authorizations?[0];
        if (earlyAuth?.Id != null)
        {
            return new PayPalAuthorizeResult
            {
                PayPalOrderId = createdOrder.Id ?? earlyAuth.Id,
                AuthorizationId = earlyAuth.Id,
                AuthorizationStatus = earlyAuth.Status?.Value ?? "CREATED"
            };
        }

        return await AuthorizeOrderAsync(createdOrder.Id!, $"{idempotencyKey}-auth", ct);
    }

    private async Task<PayPalAuthorizeResult> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken ct)
    {
        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        if (authResponse.Status == OrderStatus.PayerActionRequired)
            throw new PayPalException("Card requires browser approval; contact merchant account support.", statusCode: 400);

        var authId = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
            ?? throw new PayPalException("PayPal did not return an authorization ID.", statusCode: 502);
        var authStatus = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Status?.Value ?? "UNKNOWN";

        return new PayPalAuthorizeResult
        {
            PayPalOrderId = authResponse.Id ?? payPalOrderId,
            AuthorizationId = authId,
            AuthorizationStatus = authStatus
        };
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        CapturedPayment capture;
        string activeAuthId = authorizationId;

        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: activeAuthId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError>)
        {
            // On any 4xx/5xx, attempt re-authorization
            PaymentAuthorization reauth;
            try
            {
                reauth = await _client.Payments.ReauthorizePayment(
                    authorizationId: activeAuthId,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=minimal",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<ReauthorizePaymentError> reauthEx)
            {
                var reauthMsg = ExtractReauthorizeError(reauthEx);
                throw new PayPalAuthorizationRenewException(
                    $"Authorization is stale and cannot be renewed: {reauthMsg}. A new payment is required.");
            }
            catch (Exception reauthEx) when (reauthEx is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("Unable to reach PayPal during re-authorization.", inner: reauthEx);
            }

            activeAuthId = reauth.Id ?? throw new PayPalException("Re-authorization returned no ID.", statusCode: 502);

            try
            {
                capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: activeAuthId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-retry",
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> retryEx)
            {
                throw TranslateCaptureError(retryEx);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        var newAuthId = activeAuthId == authorizationId ? null : activeAuthId;
        var breakdown = capture.SellerReceivableBreakdown;

        return new PayPalCaptureResult
        {
            CaptureId = capture.Id ?? "",
            CaptureStatus = capture.Status?.Value ?? "UNKNOWN",
            CapturedAmount = capture.Amount?.Value ?? "",
            Currency = capture.Amount?.CurrencyCode ?? _currency,
            PayPalFee = breakdown?.PaypalFee?.Value,
            NetAmount = breakdown?.NetAmount?.Value,
            NewAuthorizationId = newAuthId
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error error))
            {
                var msg = ExtractErrorMessage(error);
                throw new PayPalException(msg, statusCode: 409);
            }
            else if (ex.Error.TryGetNoContent(out RawError noContent))
            {
                throw new PayPalException("PayPal returned an unexpected error voiding the authorization.", statusCode: 500);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw new PayPalException($"Failed to void authorization: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
            }
            throw new PayPalException("Failed to void authorization.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = amount.Value.ToString("F2", CultureInfo.InvariantCulture)
                }
            }
            : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error error))
            {
                // 409 means already refunded with this idempotency key — check details
                var msg = ExtractErrorMessage(error);
                throw new PayPalException(msg, statusCode: 409);
            }
            else if (ex.Error.TryGetNoContent(out RawError _))
            {
                throw new PayPalException("PayPal returned an unexpected error processing the refund.", statusCode: 500);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw new PayPalException($"Refund failed: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
            }
            throw new PayPalException("Refund failed.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        return new PayPalRefundResult
        {
            RefundId = refund.Id ?? "",
            RefundStatus = refund.Status?.Value ?? "UNKNOWN",
            Amount = refund.Amount?.Value,
            Currency = refund.Amount?.CurrencyCode
        };
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        string merchantCustomerId, PayPalCardDetails card, CancellationToken ct = default)
    {
        var setupBody = new SetupTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            }
        };

        SetupTokenResponse setupToken;
        try
        {
            setupToken = await _client.Vault.CreateSetupToken(
                payPalRequestId: null,
                body: setupBody,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            throw TranslateCreateSetupTokenError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        var tokenBody = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupToken.Id!,
                    Type = VaultTokenRequestType.SetupToken
                }
            }
        };

        PaymentTokenResponse paymentToken;
        try
        {
            paymentToken = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: tokenBody,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateCreatePaymentTokenError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }

        var cardInfo = paymentToken.PaymentSource?.Card;
        return new PayPalVaultResult
        {
            PaymentTokenId = paymentToken.Id ?? throw new PayPalException("PayPal did not return a payment token ID.", statusCode: 502),
            PayPalCustomerId = paymentToken.Customer?.Id,
            Last4 = cardInfo?.LastDigits,
            Brand = cardInfo?.Brand?.Value,
            Expiry = cardInfo?.Expiry
        };
    }

    public async Task DeleteVaultTokenAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateDeletePaymentTokenError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> GetTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var all = new List<PayPalTransactionRecord>();
        int page = 1;

        do
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
                    pageSize: 500,
                    page: page,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                RawError raw = ex.Error;
                throw new PayPalException(
                    $"Transaction search failed (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}",
                    statusCode: (int)raw.StatusCode);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PayPalException("PayPal returned an unreadable response.", inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("Unable to reach PayPal. Please try again.", inner: ex);
            }

            if (response.TransactionDetails != null)
            {
                foreach (var td in response.TransactionDetails)
                {
                    var info = td.TransactionInfo;
                    all.Add(new PayPalTransactionRecord
                    {
                        TransactionId = info?.TransactionId,
                        Status = info?.TransactionStatus,
                        Amount = info?.TransactionAmount?.Value,
                        Currency = info?.TransactionAmount?.CurrencyCode,
                        Fee = info?.FeeAmount?.Value,
                        InitiationDate = info?.TransactionInitiationDate,
                        PayPalReferenceId = info?.PaypalReferenceId
                    });
                }
            }

            if ((response.TotalPages ?? 0) <= page)
                break;

            page++;
        } while (true);

        return all;
    }

    // ── Error translation helpers ────────────────────────────────────────────

    private static PayPalException TranslateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
            return new PayPalException(ExtractErrorMessage(error), statusCode: 400);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"PayPal error: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Failed to create PayPal order.");
    }

    private static PayPalException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
            return new PayPalException(ExtractErrorMessage(error), statusCode: 400);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"PayPal error: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Failed to authorize PayPal order.");
    }

    private static PayPalException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
            return new PayPalException(ExtractErrorMessage(error), statusCode: 422);
        if (ex.Error.TryGetNoContent(out RawError _))
            return new PayPalException("PayPal returned an internal error during capture.", statusCode: 500);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"Capture failed: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Capture failed.");
    }

    private static string ExtractReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
            return ExtractErrorMessage(error);
        if (ex.Error.TryGetNoContent(out RawError _))
            return "Internal PayPal error.";
        if (ex.Error.TryGetRawError(out RawError raw))
            return raw.ReadAsString();
        return "Unknown error.";
    }

    private static PayPalException TranslateCreateSetupTokenError(SdkException<CreateSetupTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error1))
            return new PayPalException(ExtractError1Message(error1), statusCode: 400);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"PayPal vault error: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Failed to create vault setup token.");
    }

    private static PayPalException TranslateCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error1))
            return new PayPalException(ExtractError1Message(error1), statusCode: 400);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"PayPal vault error: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Failed to create payment token.");
    }

    private static PayPalException TranslateDeletePaymentTokenError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error1))
            return new PayPalException(ExtractError1Message(error1), statusCode: 400);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"PayPal vault error: {raw.ReadAsString()}", statusCode: (int)raw.StatusCode);
        return new PayPalException("Failed to delete payment token.");
    }

    private static string ExtractErrorMessage(Error error)
    {
        var issues = error.Details?
            .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList();
        return issues?.Count > 0 ? string.Join("; ", issues) : error.Message;
    }

    private static string ExtractError1Message(Error1 error)
    {
        return error.Message;
    }
}
