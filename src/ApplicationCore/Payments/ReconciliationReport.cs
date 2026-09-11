using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation of PayPal's own record of transactions in a date range against eShop's payment
/// records, so a payment PayPal knows about but eShop does not — or the reverse — is visible.
/// </summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>Every PayPal transaction in range, each matched (or not) to an eShop payment.</summary>
    public required IReadOnlyList<ReconciliationLine> PayPalTransactions { get; init; }

    /// <summary>eShop payments in range that PayPal reporting has no transaction for.</summary>
    public required IReadOnlyList<UnmatchedEShopPayment> EShopPaymentsWithoutPayPalRecord { get; init; }

    public int TotalPayPalTransactions => PayPalTransactions.Count;
    public int MatchedCount { get; init; }
    public int UnmatchedPayPalCount { get; init; }
}

public record ReconciliationLine
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? CustomId { get; init; }
    public string? InvoiceId { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }

    /// <summary>True when this PayPal transaction lines up with an eShop payment record.</summary>
    public bool Matched { get; init; }

    /// <summary>The eShop order id this transaction was matched to, if any.</summary>
    public int? EShopOrderId { get; init; }
}

public record UnmatchedEShopPayment
{
    public int OrderId { get; init; }
    public string PaymentReference { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? PayPalCaptureId { get; init; }
}
