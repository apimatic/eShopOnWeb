using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankCheckoutStatus
{
    Open = 0,
    Confirmed = 1
}

/// <summary>A pay-by-bank checkout: started, possibly re-priced, then confirmed into an order.</summary>
public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public BankCheckoutStatus Status { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal Total => Subtotal + ShippingCost;
}

public class BankCheckoutItem : BaseEntity
{
    public Guid CheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
