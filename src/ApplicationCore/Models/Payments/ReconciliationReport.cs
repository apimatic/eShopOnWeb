using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Lines up the processor's own record of transactions against eShop orders/payments
/// over a date range, surfacing entries known to only one side.
/// </summary>
public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}

public sealed class ReconciliationEntry
{
    public const string Matched = "Matched";
    public const string MissingInEShop = "MissingInEShop";
    public const string MissingInPayPal = "MissingInPayPal";

    public string MatchStatus { get; set; } = Matched;

    // PayPal side
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? PayPalReferenceIdType { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? TransactionAmount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? TransactionInitiatedAt { get; set; }

    // eShop side
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal? PaymentAmount { get; set; }
}
