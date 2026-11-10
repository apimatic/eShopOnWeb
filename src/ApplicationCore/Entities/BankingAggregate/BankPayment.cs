using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

public enum BankPaymentStatus
{
    /// <summary>Checkout started; nothing has been charged.</summary>
    Started = 0,

    /// <summary>Charged through Plaid and the order exists.</summary>
    Paid = 1,

    /// <summary>The order was cancelled and the money returned (or the transfer stopped before moving).</summary>
    Cancelled = 2
}

/// <summary>A pay-by-bank checkout: from "start" through "confirm" to (maybe) cancel.</summary>
public class BankPayment : BaseEntity, IAggregateRoot
{
    public Guid CheckoutId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string PlaidAccountId { get; set; } = string.Empty;
    public BankPaymentStatus Status { get; set; }

    /// <summary>JSON snapshot of the lines being bought (catalog item, name, picture, unit price, units).</summary>
    public string ItemsJson { get; set; } = "[]";
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Set when confirmed
    public string? Speed { get; set; }
    public decimal Fee { get; set; }
    public decimal ChargedAmount { get; set; }
    public string? AuthorizationId { get; set; }
    public string? TransferId { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    // Set when cancelled / shipped
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RefundId { get; set; }
}
