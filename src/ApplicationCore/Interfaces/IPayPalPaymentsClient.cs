using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record PayPalMoneyAmount(decimal Value, string Currency);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime);

public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PaypalFee,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCard(
    string PaymentTokenId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName,
    string? PayPalCustomerId);

public record PayPalReportedTransaction(
    string TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationTime,
    decimal? Amount,
    string? Currency,
    decimal? Fee);

public interface IPayPalPaymentsClient
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string invoiceId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string invoiceId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
        string? paypalCustomerId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
