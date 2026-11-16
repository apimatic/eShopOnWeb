using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixPaymentGateway
{
    Task<string> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(
        string identityId,
        FinixCard card,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> CreateAuthorizationAsync(
        string sourcePaymentInstrumentId,
        long amountCents,
        string currency,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult?> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResult?> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<decimal?> GetTransferFeeCentsAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResult> RefundTransferAsync(
        string transferId,
        long refundAmountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorizationResult>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record FinixAddress(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);

public record FinixCard(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name,
    FinixAddress Address);

public record FinixPaymentInstrumentResult(
    string Id,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? IdentityId);

public record FinixAuthorizationResult(
    string Id,
    string? State,
    long Amount,
    string? Currency,
    string? Source,
    string? TransferId,
    DateTimeOffset? ExpiresAt,
    bool IsVoid,
    string? VoidState,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyDictionary<string, string>? Tags,
    DateTimeOffset? CreatedAt);

public record FinixTransferResult(
    string Id,
    string? State,
    long Amount,
    string? Currency,
    string? Type,
    string? OperationKey,
    string? ParentTransfer,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, string>? Tags,
    string? FailureCode,
    string? FailureMessage);
