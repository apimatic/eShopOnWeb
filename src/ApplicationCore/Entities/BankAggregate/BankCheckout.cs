using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankCheckoutStatus
{
    public const string Open = "open";
    public const string Completed = "completed";
}

/// <summary>A started bank payment, waiting for the shopper to confirm it.</summary>
public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? EmailAddress { get; set; }
    public int BankConnectionId { get; set; }
    public decimal ShippingCost { get; set; }
    public string Status { get; set; } = BankCheckoutStatus.Open;
    public int? OrderId { get; set; }
    public string ShipStreet { get; set; } = string.Empty;
    public string ShipCity { get; set; } = string.Empty;
    public string ShipState { get; set; } = string.Empty;
    public string ShipCountry { get; set; } = string.Empty;
    public string ShipZipCode { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Units);
    public decimal Total => Subtotal + ShippingCost;
}

public class BankCheckoutItem : BaseEntity
{
    public Guid BankCheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
