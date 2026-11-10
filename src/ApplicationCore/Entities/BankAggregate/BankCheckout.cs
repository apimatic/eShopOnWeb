using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankCheckoutStatus
{
    Started = 0,
    /// <summary>The bank was charged; the order is about to be recorded.</summary>
    Charged = 1,
    Confirmed = 2,
    Cancelled = 3,
    Abandoned = 4
}

/// <summary>
/// A pay-by-bank checkout and, once confirmed, the payment behind an order.
/// </summary>
public class BankCheckout : BaseEntity, IAggregateRoot
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string ShopperId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string ItemsJson { get; set; } = "[]";
    public string? ShipToJson { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public string Currency { get; set; } = "USD";
    public BankCheckoutStatus Status { get; set; }

    /// <summary>
    /// JSON list of the bank transfers making up the payment (one, or several when the amount exceeds the ACH limit
    /// of a single transfer), each with its Plaid authorization and transfer ids.
    /// </summary>
    public string? ChargesJson { get; set; }

    public string? Speed { get; set; }
    public decimal Fee { get; set; }
    public decimal ChargedAmount { get; set; }
    /// <summary>The first (or only) bank transfer.</summary>
    public string? TransferId { get; set; }
    public DateTime? ArrivesOn { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public decimal RefundedAmount { get; set; }
}
