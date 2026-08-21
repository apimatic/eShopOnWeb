using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalPaymentsGateway
{
    Task<PayPalCreatedOrder> CreateAuthorizeOrderAsync(
        PayPalCreateOrderRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> AuthorizeOrderAsync(
        string payPalOrderId,
        PayPalCardPaymentSource paymentSource,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        PayPalMoney amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCapture> CaptureAuthorizationAsync(
        string authorizationId,
        PayPalCaptureRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCapture> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefund> RefundCaptureAsync(
        string captureId,
        PayPalMoney? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardPaymentSource card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);
}

public sealed record PayPalMoney(string CurrencyCode, string Value);

public sealed record PayPalCreateOrderRequest(
    PayPalMoney Amount,
    string InvoiceId,
    string CustomId,
    string? Description);

public sealed record PayPalCardPaymentSource(
    string? Number,
    string? Expiry,
    string? SecurityCode,
    string? Name,
    string? VaultId,
    PayPalBillingAddress? BillingAddress,
    bool IsStoredCredential);

public sealed record PayPalBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public sealed record PayPalCaptureRequest(
    PayPalMoney? Amount,
    bool FinalCapture,
    string? InvoiceId);

public sealed record PayPalCreatedOrder(string Id, string Status);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime,
    string? AmountValue,
    string? CurrencyCode);

public sealed record PayPalCapture(
    string CaptureId,
    string Status,
    string? AmountValue,
    string? CurrencyCode,
    string? PayPalFeeValue,
    string? NetAmountValue);

public sealed record PayPalRefund(
    string RefundId,
    string Status,
    string? AmountValue,
    string? CurrencyCode);

public sealed record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string? Expiry);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    string? AmountValue,
    string? CurrencyCode,
    string? FeeValue,
    DateTimeOffset? InitiationDate);
