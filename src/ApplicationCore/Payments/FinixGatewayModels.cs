using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record FinixPaymentInstrument(
    string Id,
    string Brand,
    string Last4,
    int ExpirationMonth,
    int ExpirationYear,
    bool Enabled);

public sealed record FinixAuthorization(
    string Id,
    string State,
    long AmountMinor,
    string Currency,
    string? TransferId,
    string? VoidState,
    bool IsVoid,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? CreatedAt);

public sealed record FinixTransfer(
    string Id,
    string State,
    string Type,
    long AmountMinor,
    string Currency,
    string? ParentTransferId,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? CreatedAt,
    string? OrderReference);

public sealed record FinixFeeSummary(decimal TotalFeeMinor, string Currency);

public sealed record FinixRefund(
    string Id,
    string State,
    long AmountMinor,
    string Currency,
    string? ParentTransferId,
    string? FailureCode,
    string? FailureMessage,
    bool IdempotentReplay = false);
