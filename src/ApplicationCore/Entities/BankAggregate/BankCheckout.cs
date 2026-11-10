using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankCheckoutStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2
}

public class BankCheckoutItem
{
    public int Id { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>A started bank payment, waiting for the shopper to confirm a speed.</summary>
public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public decimal ShippingCost { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public BankCheckoutStatus Status { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal Subtotal() => Items.Sum(i => i.UnitPrice * i.Units);
    public decimal Total() => Subtotal() + ShippingCost;
}
