using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record FinixPaymentInstrumentInfo(
    string Id,
    string? IdentityId,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? CardType);

public sealed record FinixAuthorizationInfo(
    string Id,
    string? State,
    long Amount,
    string? Source,
    string? TransferId,
    bool IsVoid,
    DateTimeOffset? ExpiresAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransferInfo(
    string Id,
    string? State,
    long? Amount,
    string? Type,
    string? ParentTransferId,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Tags,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixRefundInfo(
    string Id,
    string? State,
    long Amount,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransactionRecord(
    string Id,
    string Kind,
    string? State,
    long? Amount,
    string? Type,
    string? ParentId,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Tags,
    string? FailureCode,
    string? FailureMessage);

public interface IFinixPaymentGateway
{
    Task<FinixPaymentInstrumentInfo> CreateCardPaymentInstrumentAsync(
        string? buyerEmail,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentInfo?> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationInfo> CreateAuthorizationAsync(
        long amountCents,
        string currency,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        string orderId,
        string operationId,
        string? securityCode,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationInfo> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationInfo> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationInfo> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferInfo> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<decimal> GetFeeCentsAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixRefundInfo> RefundTransferAsync(
        string transferId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
