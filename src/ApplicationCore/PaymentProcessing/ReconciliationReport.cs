using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public record ReconciliationEntry(
    int? OrderId,
    string? PayPalTransactionId,
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string? EShopStatus,
    string? PayPalStatus,
    string MatchStatus);

public static class ReconciliationMatchStatus
{
    public const string Matched = "Matched";
    public const string PayPalOnly = "PayPalOnly";
    public const string EShopOnly = "EShopOnly";
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries,
    IReadOnlyList<string> Warnings);
