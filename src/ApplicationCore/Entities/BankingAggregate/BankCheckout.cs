using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

/// <summary>
/// A pay-by-bank checkout: started by the shopper, optionally re-priced, then confirmed
/// (which charges the bank account and creates the order) and possibly cancelled.
/// </summary>
public class BankCheckout : BaseEntity, IAggregateRoot
{
    public const string STATUS_STARTED = "started";
    public const string STATUS_CONFIRMED = "confirmed";
    public const string STATUS_CANCELLED = "cancelled";

    public Guid CheckoutId { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }

    /// <summary>JSON array of {catalogItemId, unitPrice, units}.</summary>
    public string ItemsJson { get; set; } = "[]";
    public string ShipToJson { get; set; } = "{}";

    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }

    public string Status { get; set; } = STATUS_STARTED;

    // Set when confirmed
    public string? Speed { get; set; }
    public decimal Fee { get; set; }
    public decimal TotalCharged { get; set; }
    public string? AuthorizationId { get; set; }
    public string? TransferId { get; set; }
    public int? OrderId { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    // Set when cancelled
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RefundId { get; set; }

    /// <summary>Set when the order ships; a shipped order can no longer be cancelled.</summary>
    public DateTimeOffset? ShippedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public decimal Total => Subtotal + ShippingCost;
}
