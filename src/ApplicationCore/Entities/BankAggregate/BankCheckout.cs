using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class PaymentSpeeds
{
    public const string Standard = "standard";
    public const string Express = "express";
}

public static class CheckoutStatuses
{
    public const string Open = "open";
    public const string Confirmed = "confirmed";
}

public static class BankOrderStatuses
{
    public const string Paid = "paid";
    public const string Shipped = "shipped";
    public const string Cancelled = "cancelled";
}

/// <summary>A pay-by-bank checkout started by a shopper and not necessarily confirmed yet.</summary>
public class BankCheckout : BaseEntity, IAggregateRoot
{
    public string PublicId { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerKey { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public int BankConnectionId { get; set; }
    public string PlaidAccountId { get; set; } = string.Empty;
    public decimal ShippingCost { get; set; }
    public string Status { get; set; } = CheckoutStatuses.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Speed the stored authorizations were created for (reused by an identical retry).</summary>
    public string? AuthorizationSpeed { get; set; }
    public List<BankCheckoutAuthorization> Authorizations { get; set; } = new();

    public int? OrderId { get; set; }
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    public decimal Total => Subtotal + ShippingCost;
}

/// <summary>One Plaid transfer authorization of a checkout; a large charge is split into several.</summary>
public class BankCheckoutAuthorization : BaseEntity
{
    public int BankCheckoutId { get; set; }
    public int Part { get; set; }
    public decimal Amount { get; set; }
    public string AuthorizationId { get; set; } = string.Empty;
    /// <summary>Set once the authorization has been turned into a transfer.</summary>
    public string? TransferId { get; set; }
    public string? ExpectedArrival { get; set; }
}

public class BankCheckoutItem : BaseEntity
{
    public int BankCheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>An order paid by bank.</summary>
public class BankOrder : BaseEntity, IAggregateRoot
{
    public string OwnerKey { get; set; } = string.Empty;
    public string CheckoutPublicId { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Fee { get; set; }
    /// <summary>Everything the shopper was charged: subtotal + shipping + fee.</summary>
    public decimal Total { get; set; }
    public string Speed { get; set; } = PaymentSpeeds.Standard;
    public DateTime ArrivesOn { get; set; }
    public string Status { get; set; } = BankOrderStatuses.Paid;
    public List<BankOrderTransfer> Transfers { get; set; } = new();
    public DateTimeOffset PlacedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

/// <summary>A Plaid transfer that paid (part of) an order.</summary>
public class BankOrderTransfer : BaseEntity
{
    public int BankOrderId { get; set; }
    public string TransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    /// <summary>True once the transfer was cancelled or refunded.</summary>
    public bool Reversed { get; set; }
    public string? RefundId { get; set; }
}
