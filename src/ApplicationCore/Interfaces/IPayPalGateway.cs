using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currencyCode,
        string invoiceId,
        string customId,
        CardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currencyCode,
        string invoiceId,
        string customId,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<PayPalVoidResult> VoidAsync(string authorizationId, string payPalRequestId, CancellationToken cancellationToken);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task<PayPalVaultResult> VaultCardAsync(
        string merchantCustomerId,
        CardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? currencyCode,
        CancellationToken cancellationToken);
}

public sealed class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public string? OrderStatus { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed class PayPalVoidResult
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class PayPalVaultResult
{
    public required string PaymentTokenId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalTransactionRecord
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? PaypalReferenceIdType { get; init; }
    public string? TransactionEventCode { get; init; }
    public DateTimeOffset? TransactionInitiationDate { get; init; }
    public DateTimeOffset? TransactionUpdatedDate { get; init; }
    public decimal? TransactionAmount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? TransactionStatus { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? PaymentTrackingId { get; init; }
}
