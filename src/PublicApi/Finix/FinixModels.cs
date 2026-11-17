using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed record CardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name);

public sealed record FinixIdentity(string Id);

public sealed record FinixInstrument(
    string Id,
    string? LastFour,
    string? Brand,
    int? ExpirationMonth,
    int? ExpirationYear,
    bool? Enabled,
    string? Fingerprint,
    string? Name);

public sealed record FinixAuthorization(
    string Id,
    long? Amount,
    string? State,
    string? TransferId,
    bool? IsVoid,
    string? VoidState,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? CreatedAt);

public sealed record FinixTransfer(
    string Id,
    long? Amount,
    string? State,
    string? Type,
    string? ParentTransferId,
    long? Fee,
    DateTimeOffset? CreatedAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixPage<T>(IReadOnlyList<T> Items, bool Complete, int PagesWalked);

public sealed record FinixFeeSummary(long FeeCents, int FeeCount, bool Complete, int PagesWalked);
