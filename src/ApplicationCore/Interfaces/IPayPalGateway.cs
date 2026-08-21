using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalVaultResult> SaveCardAsync(
        string buyerId,
        CardPaymentInput card,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<PayPalCaptureResult?> FindCaptureForPayPalOrderAsync(string payPalOrderId, CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public record CardPaymentInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? Expiration,
    bool PayerActionRequired,
    PayPalCaptureResult? ExistingCapture = null);

public record PayPalCaptureResult(
    string CaptureId,
    string? Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount);

public record PayPalRefundResult(
    string RefundId,
    string? Status,
    decimal Amount);

public record PayPalVaultResult(
    string TokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record PayPalReportedTransaction(
    string? TransactionId,
    string? Status,
    string? Amount,
    string? Currency,
    string? FeeAmount,
    string? InitiationDate,
    string? UpdatedDate,
    string? InvoiceId,
    string? CustomField,
    string? PaypalReferenceId,
    string? PaypalReferenceIdType);
