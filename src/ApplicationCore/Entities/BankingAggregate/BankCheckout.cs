using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

public enum BankCheckoutStatus
{
    Open = 0,
    Confirmed = 1
}

/// <summary>A started pay-by-bank checkout, until the shopper confirms it.</summary>
public class BankCheckout
{
    /// <summary>Opaque identifier (GUID without dashes).</summary>
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }

    /// <summary>JSON snapshot of the items (catalog id, name, unit price, units) at the time of starting.</summary>
    public string ItemsJson { get; set; } = "[]";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public BankCheckoutStatus Status { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public decimal Total => Subtotal + Shipping;
}
