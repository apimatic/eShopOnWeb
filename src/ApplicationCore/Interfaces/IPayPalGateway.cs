using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PayPalMoney(string Currency, decimal Value);

public record PayPalCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public record PayPalAuthorizationResult(
    string PaypalOrderId,
    string PaypalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public record PayPalCaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string RefundStatus,
    decimal Amount,
    string Currency);

public record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

public record PayPalTransactionRecord(
    string TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? InitiationDate);

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default);
}
