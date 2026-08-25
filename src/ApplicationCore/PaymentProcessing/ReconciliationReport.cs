using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public static class ReconciliationMatchState
{
    public const string Matched = "Matched";
    public const string PayPalOnly = "PayPalOnly";
    public const string EShopOnly = "EShopOnly";
}

public record ReconciliationEntry(
    string? PayPalTransactionId, decimal? PayPalAmount, string? PayPalStatus,
    int? OrderId, decimal? LocalAmount, string? LocalDescription, string MatchState);

public record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Entries);
