using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Banking;

public enum BankCheckoutStatus
{
    Started = 0,
    Confirming = 1,
    Confirmed = 2
}

/// <summary>A pay-by-bank checkout that was started and may be confirmed later.</summary>
public class BankCheckout : IAggregateRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    /// <summary>JSON array of { catalogItemId, quantity }.</summary>
    public string ItemsJson { get; set; } = "[]";
    public string? ShipStreet { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipZipCode { get; set; }
    public decimal ShippingCost { get; set; }
    public BankCheckoutStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Speed { get; set; }
    public decimal Fee { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? ConfirmingSince { get; set; }
    /// <summary>Concurrency token, bumped on every change so a checkout can't be confirmed twice at once.</summary>
    public int Revision { get; set; }
}
