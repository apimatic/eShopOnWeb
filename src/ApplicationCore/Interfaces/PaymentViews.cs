using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item on a new order: a catalog item and how many of it.</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressInput(
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

/// <summary>A refund as shown to a caller.</summary>
public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A single line of a placed order.</summary>
public class OrderLineView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order together with its payment state, as returned by the API.</summary>
public class OrderPaymentView
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public decimal Total { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public List<OrderLineView> Items { get; set; } = new();
    public List<RefundView> Refunds { get; set; } = new();
}

/// <summary>The result of issuing a refund.</summary>
public record RefundOutcome(string RefundId, OrderPaymentView Order);

/// <summary>One PayPal transaction lined up against an eShop order.</summary>
public class ReconciliationMatch
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public int OrderId { get; set; }
    public decimal PayPalAmount { get; set; }
    public string? Status { get; set; }
    public string MatchedBy { get; set; } = string.Empty; // capture | refund | authorization | custom_id
}

/// <summary>An eShop payment record with no corresponding PayPal transaction found in the range.</summary>
public class EShopUnmatched
{
    public int OrderId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// A reconciliation report over a date range: PayPal's own transaction records lined up against
/// eShop orders, surfacing transactions PayPal knows about that eShop does not (and the reverse).
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public int PayPalTransactionCount { get; set; }
    public decimal PayPalGrossTotal { get; set; }

    /// <summary>Transactions present in PayPal that matched an eShop order.</summary>
    public List<ReconciliationMatch> Matched { get; set; } = new();

    /// <summary>Transactions PayPal knows about that eShop could not match to an order.</summary>
    public List<PayPalTransaction> PayPalOnly { get; set; } = new();

    /// <summary>eShop payments in the range with no PayPal transaction found (subject to reporting lag).</summary>
    public List<EShopUnmatched> EShopOnly { get; set; } = new();

    /// <summary>True when the requested range was empty of PayPal transactions (expected under sandbox reporting lag).</summary>
    public bool PayPalRangeEmpty => PayPalTransactionCount == 0;
}
