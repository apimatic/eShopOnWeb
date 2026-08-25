using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(int orderId, decimal amount, string currency, CardDetails card, CancellationToken ct = default)
    {
        var payPalOrder = await CreateOrderInternalAsync(orderId, amount, currency, ct);
        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode
                }
            }
        };
        return await AuthorizeOrderInternalAsync(payPalOrder.Id!, orderId, authorizeRequest, ct);
    }

    public async Task<AuthorizeResult> AuthorizeWithVaultAsync(int orderId, decimal amount, string currency, string vaultId, CancellationToken ct = default)
    {
        var payPalOrder = await CreateOrderInternalAsync(orderId, amount, currency, ct);
        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest { VaultId = vaultId }
            }
        };
        return await AuthorizeOrderInternalAsync(payPalOrder.Id!, orderId, authorizeRequest, ct);
    }

    private async Task<Order> CreateOrderInternalAsync(int orderId, decimal amount, string currency, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: orderId.ToString(),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = amount.ToString("F2")
                            },
                            InvoiceId = orderId.ToString()
                        }
                    }
                },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal create-order error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    private async Task<AuthorizeResult> AuthorizeOrderInternalAsync(string payPalOrderId, int eShopOrderId, OrderAuthorizeRequest authorizeRequest, CancellationToken ct)
    {
        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: eShopOrderId.ToString(),
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeRequest,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            if (response.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException(
                    "PayPal requires payer action (3DS challenge). Browser redirect is required and is not supported by this integration.",
                    PayPalErrorKind.PayerActionRequired);

            var auth = response.PurchaseUnits?[0]?.Payments?.Authorizations?[0]
                ?? throw new PayPalException("PayPal authorization response is missing authorization data.", PayPalErrorKind.General);

            var expiresAt = DateTimeOffset.TryParse(auth.ExpirationTime, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.AddDays(3);

            return new AuthorizeResult(payPalOrderId, auth.Id!, expiresAt);
        }
        catch (PayPalException) { throw; }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal authorize-order error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task<CaptureResult> CaptureAsync(int orderId, string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{orderId}",
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var breakdown = response.SellerReceivableBreakdown;
            var capturedAmount = decimal.TryParse(response.Amount?.Value, out var ca) ? ca : 0m;
            var feeAmount = decimal.TryParse(breakdown?.PaypalFee?.Value, out var fee) ? fee : (decimal?)null;
            var netAmount = decimal.TryParse(breakdown?.NetAmount?.Value, out var net) ? net : (decimal?)null;

            return new CaptureResult(response.Id!, capturedAmount, feeAmount, netAmount);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetNoContent(out _)) msg = "PayPal returned empty error response for capture";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal capture error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetNoContent(out _)) msg = "PayPal returned empty error response for void";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal void error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(int orderId, string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = amount.ToString("F2") }
                },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var newAuthId = response.Id ?? authorizationId;
            return new ReauthorizeResult(newAuthId, DateTimeOffset.UtcNow.AddDays(3));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            string msg = "Re-authorization is not possible; create a new authorization instead.";
            if (ex.Error.TryGetError(out var typed))
                msg = $"Re-authorization failed ({typed.Name}): {typed.Message}. Create a new authorization.";
            else if (ex.Error.TryGetNoContent(out _))
                msg = "Re-authorization returned an empty error. Create a new authorization.";
            else if (ex.Error.TryGetRawError(out var raw))
                msg = $"Re-authorization failed: {raw.ReadAsString()}. Create a new authorization.";
            throw new PayPalException(msg, PayPalErrorKind.ReauthorizationImpossible, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string? currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            Money? refundAmount = amount.HasValue && currency != null
                ? new Money { CurrencyCode = currency, Value = amount.Value.ToString("F2") }
                : null;

            var response = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new RefundRequest { Amount = refundAmount },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var refundedAmount = decimal.TryParse(response.Amount?.Value, out var ra) ? ra : (amount ?? 0m);
            return new RefundResult(response.Id!, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetNoContent(out _)) msg = "PayPal returned empty error response for refund";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal refund error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(string startDate, string endDate, CancellationToken ct = default)
    {
        var all = new List<TransactionRecord>();
        try
        {
            var first = await _client.TransactionSearch.SearchTransactions(
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
                page: 1,
                requestOptions: null,
                ct: ct);

            AppendTransactions(all, first);

            var totalPages = first.TotalPages ?? 1;
            for (var page = 2; page <= totalPages; page++)
            {
                var next = await _client.TransactionSearch.SearchTransactions(
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
                    requestOptions: null,
                    ct: ct);
                AppendTransactions(all, next);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalException($"PayPal transaction search failed: {ex.Error.ReadAsString()}", PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }

        return all;
    }

    private static void AppendTransactions(List<TransactionRecord> list, SearchResponse response)
    {
        if (response.TransactionDetails == null) return;
        foreach (var detail in response.TransactionDetails)
        {
            var info = detail.TransactionInfo;
            if (info == null) continue;
            var amount = decimal.TryParse(info.TransactionAmount?.Value, out var a) ? a : (decimal?)null;
            var fee = decimal.TryParse(info.FeeAmount?.Value, out var f) ? f : (decimal?)null;
            list.Add(new TransactionRecord(
                info.TransactionId ?? string.Empty,
                info.PaypalReferenceId,
                info.TransactionStatus,
                amount,
                fee,
                info.InvoiceId,
                info.TransactionInitiationDate));
        }
    }

    public async Task<VaultResult> VaultCardAsync(string merchantCustomerId, CardDetails card, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"vault-{merchantCustomerId}-{Guid.NewGuid():N}",
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode
                        }
                    }
                },
                requestOptions: null,
                ct: ct);

            var cardEntity = response.PaymentSource?.Card;
            return new VaultResult(
                response.Id!,
                response.Customer?.Id,
                cardEntity?.LastDigits,
                cardEntity?.Brand?.Value,
                cardEntity?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal vault error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }

    public async Task<IReadOnlyList<VaultedCard>> ListVaultedCardsAsync(string payPalCustomerId, CancellationToken ct = default)
    {
        var all = new List<VaultedCard>();
        try
        {
            var first = await _client.Vault.ListCustomerPaymentTokens(
                customerId: payPalCustomerId,
                pageSize: 20,
                page: 1,
                totalRequired: true,
                requestOptions: null,
                ct: ct);

            AppendVaultedCards(all, first);

            var totalPages = first.TotalPages ?? 1;
            for (var page = 2; page <= totalPages; page++)
            {
                var next = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: payPalCustomerId,
                    pageSize: 20,
                    page: page,
                    totalRequired: false,
                    requestOptions: null,
                    ct: ct);
                AppendVaultedCards(all, next);
            }
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal vault list error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }

        return all;
    }

    private static void AppendVaultedCards(List<VaultedCard> list, CustomerVaultPaymentTokensResponse response)
    {
        if (response.PaymentTokens == null) return;
        foreach (var token in response.PaymentTokens)
        {
            var card = token.PaymentSource?.Card;
            list.Add(new VaultedCard(token.Id!, card?.LastDigits, card?.Brand?.Value, card?.Expiry));
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var typed)) msg = $"{typed.Name}: {typed.Message}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "Unknown PayPal vault delete error";
            throw new PayPalException(msg, PayPalErrorKind.General, ex);
        }
        catch (JsonException ex) { throw new PayPalException("PayPal returned an unprocessable response.", PayPalErrorKind.General, ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalException("PayPal is unreachable.", PayPalErrorKind.General, ex); }
    }
}
