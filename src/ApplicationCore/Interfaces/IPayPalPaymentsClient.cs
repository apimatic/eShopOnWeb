using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardPaymentSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public record PayPalAuthorizationResult(
    string OrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpirationTime);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PaypalFee,
    decimal? NetAmount);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCardResult(
    string PaymentTokenId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? CardholderName,
    string? PayPalCustomerId);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate);

public interface IPayPalPaymentsClient
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        string requestId,
        CardPaymentSource? card,
        string? vaultId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
