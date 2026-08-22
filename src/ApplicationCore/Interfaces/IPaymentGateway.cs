using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<PaymentAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string instanceKey,
        CancellationToken cancellationToken);

    Task<PaymentAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string instanceKey,
        CancellationToken cancellationToken);

    Task<PaymentAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<PaymentAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    Task<PaymentCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PaymentCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task<PaymentRefundResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<SavedCardResult> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken);

    Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class PaymentAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public string? Amount { get; init; }
}

public sealed class PaymentAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Expiration { get; init; }
}

public sealed class PaymentCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed class PaymentRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class SavedCardResult
{
    public required string VaultId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? LastDigits { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class GatewayTransaction
{
    public string? TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? ReferenceIdType { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
}
