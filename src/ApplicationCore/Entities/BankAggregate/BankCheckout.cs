using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankCheckoutStatus
{
    Open = 0,
    Confirming = 1,
    Confirmed = 2
}

/// <summary>A started pay-by-bank checkout, waiting to be confirmed.</summary>
public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public int BankConnectionId { get; set; }
    /// <summary>JSON list of the ordered items with the unit prices that were quoted.</summary>
    public string ItemsJson { get; set; } = "[]";
    public string? ShipToJson { get; set; }
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public BankCheckoutStatus Status { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public int Version { get; set; }
}
