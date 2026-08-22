using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    BillingAddressInput? BillingAddress);

public record BillingAddressInput(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    string? ExpirationTime,
    string? CreateTime,
    string AmountValue,
    string Currency);

public record AuthorizationSnapshot(
    string Id,
    string? Status,
    string? ExpirationTime,
    string? CreateTime,
    string? AmountValue,
    string? Currency);

public record CaptureResult(
    string CaptureId,
    string? Status,
    string? AmountValue,
    string? FeeValue,
    string? NetValue,
    string? Currency);

public record RefundGatewayResult(
    string RefundId,
    string? Status,
    string? AmountValue);

public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

public record PayPalTransactionRecord(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? AmountValue,
    string? Currency,
    string? FeeValue,
    string? EventCode,
    string? InitiationDate,
    string? PaypalReferenceId);

public interface IPayPalPaymentGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        string orderId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken ct);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string orderId,
        decimal amount,
        string currency,
        string vaultId,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken ct);

    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken ct);

    Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentInput card,
        string requestId,
        CancellationToken ct);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
