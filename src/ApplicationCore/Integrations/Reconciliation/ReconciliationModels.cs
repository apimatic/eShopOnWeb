using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Integrations.Reconciliation;

/// <summary>
/// Result of lining up PayPal's own transaction record for a date range against the
/// eShop orders/payments in the same range.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Every transaction PayPal reported for the range, matched to eShop where possible.</summary>
    public IReadOnlyList<ReconciliationTransactionRow> Transactions { get; init; } = Array.Empty<ReconciliationTransactionRow>();

    /// <summary>Every eShop order with a payment placed in the range, matched to PayPal where possible.</summary>
    public IReadOnlyList<ReconciliationEshopRow> EshopPayments { get; init; } = Array.Empty<ReconciliationEshopRow>();

    public ReconciliationSummary Summary { get; init; } = new ReconciliationSummary();
}

public class ReconciliationTransactionRow
{
    public required string TransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? PayPalReferenceIdType { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public decimal Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? PayerEmail { get; init; }
    public string? InvoiceId { get; init; }

    /// <summary>eShop order this PayPal transaction belongs to, when it could be matched.</summary>
    public int? EshopOrderId { get; init; }

    /// <summary>True when the transaction could not be tied to any eShop order (known to PayPal, not to eShop).</summary>
    public bool UnmatchedInEshop => EshopOrderId is null;
}

public class ReconciliationEshopRow
{
    public required int EshopOrderId { get; init; }
    public required string BuyerId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public decimal OrderTotal { get; init; }
    public string? Currency { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();

    /// <summary>True when none of this payment's PayPal ids appear in the transaction report.</summary>
    public bool FoundInPayPalReport { get; init; }
}

public class ReconciliationSummary
{
    public int PayPalTransactionCount { get; init; }
    public int EshopPaymentCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>PayPal transactions that no eShop order accounts for.</summary>
    public IReadOnlyList<string> InPayPalNotInEshop { get; init; } = Array.Empty<string>();

    /// <summary>eShop payments that no PayPal transaction accounts for (report lag included).</summary>
    public IReadOnlyList<int> InEshopNotInPayPal { get; init; } = Array.Empty<int>();
}
