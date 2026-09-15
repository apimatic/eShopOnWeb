using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record BillingAddressDetails(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddressDetails? Address);

public record AuthorizationHold(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt);

public record CaptureDetails(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record RefundDetails(
    string RefundId,
    string Status,
    decimal Amount);

public record VaultedCardDetails(
    string PaymentTokenId,
    string PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public record PayPalTransactionRecord(
    string? TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    string? Timestamp,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    string? PaypalReferenceId,
    string? PaypalReferenceIdType,
    string? EventCode);

public interface IPayPalGateway
{
    Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AuthorizationHold> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CaptureDetails> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<VaultedCardDetails> SaveCardAsync(
        string merchantCustomerId,
        string? existingPayPalCustomerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
