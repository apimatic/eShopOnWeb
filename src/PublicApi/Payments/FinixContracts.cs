using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardData(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name,
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public sealed record FinixInstrumentInfo(
    string Id,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    bool? Enabled);

public sealed record FinixAuthorizationInfo(
    string Id,
    DateTimeOffset? CreatedAt,
    string? State,
    bool IsVoid,
    string? VoidState,
    string? TransferId,
    DateTimeOffset? ExpiresAt,
    long? Amount,
    string? FailureCode,
    string? FailureMessage,
    string? IdempotencyId);

public sealed record FinixTransferInfo(
    string Id,
    string? State,
    long? Amount,
    long? Fee,
    string? ParentTransfer,
    string? Type,
    DateTimeOffset? CreatedAt,
    string? IdempotencyId,
    string? Source);

public sealed record FinixIdentityInfo(string Id);

public sealed record FinixListResult<T>(IReadOnlyList<T> Items, bool Truncated, string? NextCursor);
