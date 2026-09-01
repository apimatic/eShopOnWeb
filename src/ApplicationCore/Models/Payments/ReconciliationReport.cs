using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public static class ReconciliationMatchStatus
{
    public const string Matched = "Matched";
    public const string MissingInEshop = "MissingInEshop";
    public const string MissingInPayPal = "MissingInPayPal";
}

public sealed record ReconciliationEntry(
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? PayPalStatus,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? TransactionTime,
    int? OrderId,
    string MatchStatus);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
