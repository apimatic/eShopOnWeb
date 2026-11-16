using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record FinixCardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name)
{
    public string Bin => Number.Length >= 6 ? Number[..6] : Number;
    public string Last4 => Number.Length >= 4 ? Number[^4..] : Number;
}

public sealed record FinixInstrumentResult(
    string Id,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Name,
    string? IdentityId,
    bool? Enabled,
    string? Fingerprint,
    string? DisabledCode,
    string? DisabledMessage);

public sealed record FinixAuthorizationResult(
    string Id,
    long? Amount,
    string? State,
    string? Currency,
    string? TransferId,
    string? Source,
    string? FailureCode,
    string? FailureMessage,
    bool? IsVoid,
    string? VoidState,
    string? IdempotencyId,
    DateTimeOffset? ExpiresAt);

public sealed record FinixTransferResult(
    string Id,
    long? Amount,
    long? Fee,
    string? State,
    string? Type,
    string? ParentTransfer,
    DateTimeOffset? CreatedAt,
    string? FailureCode,
    string? FailureMessage,
    string? IdempotencyId);

public sealed record FinixFeeResult(long AmountCents, bool Complete);

public sealed record FinixPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool Truncated);
