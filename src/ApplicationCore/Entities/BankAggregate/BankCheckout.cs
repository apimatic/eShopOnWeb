using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankCheckoutStatus
{
    Open = 0,
    Confirmed = 1
}

/// <summary>
/// A bank payment that has been started but is not (necessarily) confirmed yet.
/// </summary>
public class BankCheckout : BaseEntity, IAggregateRoot
{
    // The public identifier of a checkout is an unguessable GUID, not the integer key.
    public Guid CheckoutId { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string UserKey { get; set; } = string.Empty;
    public int BankConnectionId { get; set; }
    public decimal ShippingCost { get; set; }
    public string ShipStreet { get; set; } = string.Empty;
    public string ShipCity { get; set; } = string.Empty;
    public string ShipState { get; set; } = string.Empty;
    public string ShipCountry { get; set; } = string.Empty;
    public string ShipZipCode { get; set; } = string.Empty;
    public BankCheckoutStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal ItemsTotal()
    {
        var total = 0m;
        foreach (var item in Items)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }
}

public class BankCheckoutItem : BaseEntity
{
    public int BankCheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
