using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationEntry> Entries { get; init; }
}

public enum ReconciliationMatchStatus
{
    Matched,
    PayPalOnly,
    EShopOnly
}

public class ReconciliationEntry
{
    public required ReconciliationMatchStatus MatchStatus { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalOrderId { get; init; }
    public int? OrderId { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? EShopAmount { get; init; }
    public string? PayPalStatus { get; init; }
    public string? EShopStatus { get; init; }
}
