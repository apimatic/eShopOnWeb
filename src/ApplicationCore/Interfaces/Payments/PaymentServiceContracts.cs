using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>One catalog line on a placed order.</summary>
public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>How an order is to be paid: a one-off raw card, or one of the shopper's saved cards.</summary>
public class PaymentInstruction
{
    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="SavedCardId"/>.</summary>
    public CardDetails? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? SavedCardId { get; set; }
}

/// <summary>A refund recorded against a captured order.</summary>
public class RefundOutcome
{
    /// <summary>PayPal's refund id; surfaced as the top-level <c>refundId</c> of the create-refund response.</summary>
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>A saved card as the shopper sees it — never full card details.</summary>
public class SavedCardSummary
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An order together with its payment state, for the shopper's order list.</summary>
public class OrderPaymentSummary
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public IReadOnlyList<OrderLineSummary> Lines { get; set; } = Array.Empty<OrderLineSummary>();
    public IReadOnlyList<RefundOutcome> Refunds { get; set; } = Array.Empty<RefundOutcome>();
}

public class OrderLineSummary
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

// --- Reconciliation ---

public enum ReconciliationMatch
{
    /// <summary>Present and agreeing on both sides.</summary>
    Matched,
    /// <summary>PayPal reports a transaction eShop has no captured order for.</summary>
    InPayPalOnly,
    /// <summary>eShop captured an order PayPal's report does not (yet) show.</summary>
    InEShopOnly
}

public class ReconciliationLine
{
    public ReconciliationMatch Match { get; set; }
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public decimal? EShopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public string? PayPalStatus { get; set; }
    public string? Note { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopCapturedCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public IReadOnlyList<ReconciliationLine> Lines { get; set; } = Array.Empty<ReconciliationLine>();
}
