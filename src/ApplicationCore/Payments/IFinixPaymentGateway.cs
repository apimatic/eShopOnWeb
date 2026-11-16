using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record FinixCardInput(
    string Name,
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public record FinixCardInfo(
    string PaymentInstrumentId,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Fingerprint);

public record FinixAuthorizationResult(
    string AuthorizationId,
    string? State,
    long AmountCents,
    string? TransferId,
    string? FailureCode,
    string? FailureMessage,
    bool? IsVoid,
    DateTimeOffset? ExpiresAt);

public record FinixCaptureResult(
    string AuthorizationId,
    string? AuthorizationState,
    string TransferId,
    string? TransferState,
    long CapturedAmountCents,
    long FeeCents,
    long NetCents);

public record FinixVoidResult(
    string AuthorizationId,
    string? State,
    bool IsVoid,
    string? VoidState);

public record FinixRefundResult(
    string RefundId,
    long AmountCents,
    string? State,
    string? ParentTransferId);

public record FinixTransaction(
    string Id,
    string? Type,
    string? State,
    long? AmountCents,
    long? FeeCents,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    string? IdempotencyId);

public record FinixTransactionPage(
    IReadOnlyList<FinixTransaction> Items,
    bool Truncated,
    string? NextCursor,
    DateTimeOffset? CoveredTo);

/// <summary>
/// The Finix call surface this application needs. Implementations translate the SDK's
/// typed errors into <see cref="PaymentOperationException"/>.
/// </summary>
public interface IFinixPaymentGateway
{
    Task<string> EnsureShopperIdentityAsync(string buyerId, string? email, CancellationToken cancellationToken);

    Task<string?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken);

    Task<FinixCardInfo> SaveCardAsync(string identityId, FinixCardInput card, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixCardInfo>> ListCardsAsync(string identityId, CancellationToken cancellationToken);

    Task<bool> CardBelongsToIdentityAsync(string identityId, string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixCardInfo?> FindCardAsync(string identityId, string last4, int expirationMonth, int expirationYear, CancellationToken cancellationToken);

    Task<FinixAuthorizationResult> AuthorizeAsync(
        string paymentInstrumentId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyAsync(string idempotencyId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixCaptureResult> CaptureAsync(string authorizationId, long amountCents, CancellationToken cancellationToken);

    Task<FinixTransaction?> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixVoidResult> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixRefundResult> RefundAsync(string transferId, long amountCents, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransaction>> ListReversalsAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
