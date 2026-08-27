using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}

public class ReconciliationEntry
{
    /// <summary>PayPal's transaction id, or null for a local payment PayPal hasn't reported.</summary>
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }

    /// <summary>The eShop order this transaction lines up with, if any.</summary>
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }

    /// <summary>Matched | MissingLocally (PayPal knows it, eShop doesn't) | MissingInPayPal (eShop knows it, PayPal doesn't).</summary>
    public string MatchStatus { get; set; } = string.Empty;
}
