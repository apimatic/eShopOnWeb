using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record FinixAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country);

public sealed record FinixCardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string? Name,
    FinixAddress? Address);

public sealed record FinixPaymentInstrument(
    string Id,
    bool Enabled,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? CardType,
    string? Name,
    string? IdentityId);

public sealed record FinixAuthorization(
    string Id,
    string? State,
    long AmountCents,
    bool IsVoid,
    string? VoidState,
    string? TransferId,
    DateTimeOffset? ExpiresAt,
    string? FailureCode,
    string? FailureMessage,
    string? NetworkBrand,
    string? Source);

public sealed record FinixTransfer(
    string Id,
    string? Type,
    string? State,
    long AmountCents,
    long FeeCents,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    string? OperationKey,
    string? Source,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransactionRecord(
    string Kind,
    string Id,
    string? Type,
    string? State,
    long AmountCents,
    long FeeCents,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    string? Merchant,
    string? Source,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyDictionary<string, string> Tags);

public interface IFinixPaymentGateway
{
    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(FinixCardDetails card, CancellationToken cancellationToken);

    Task<FinixPaymentInstrument?> GetPaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        string paymentInstrumentId,
        long amountCents,
        string currency,
        string idempotencyId,
        string orderId,
        string orderKey,
        string buyerId,
        CancellationToken cancellationToken);

    Task<FinixAuthorization?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long amountCents, CancellationToken cancellationToken);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransfer?> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransfer> CreateRefundAsync(string captureTransferId, long amountCents, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
