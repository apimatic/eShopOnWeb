using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>A pay-by-bank checkout that was started and not yet (or already) confirmed.</summary>
public class BankCheckout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    /// <summary>JSON array of the ordered items, priced from the catalog when the checkout started.</summary>
    public string ItemsJson { get; set; } = "[]";
    public string? ShipToJson { get; set; }
    public decimal ShippingCost { get; set; }
    public string Status { get; set; } = BankCheckoutStatus.Open;
    public int? OrderId { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}

public static class BankCheckoutStatus
{
    public const string Open = "open";
    public const string Completed = "completed";
}
