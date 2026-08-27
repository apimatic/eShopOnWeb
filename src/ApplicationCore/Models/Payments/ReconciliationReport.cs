using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Every transaction PayPal reported for the range, with its match status.</summary>
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();

    /// <summary>eShop payments in the range that PayPal's report does not know about.</summary>
    public List<EshopOnlyPayment> EshopOnlyPayments { get; set; } = new List<EshopOnlyPayment>();

    public int TotalPayPalTransactions { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
}

public class ReconciliationEntry
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? Fee { get; set; }
    public DateTimeOffset? Time { get; set; }

    /// <summary>"Matched" when lined up with an eShop order, "PayPalOnly" otherwise.</summary>
    public string MatchStatus { get; set; } = "PayPalOnly";
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
}

public class EshopOnlyPayment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}
