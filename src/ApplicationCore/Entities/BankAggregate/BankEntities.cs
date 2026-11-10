using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankConnectionKinds
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

public static class PaymentSpeeds
{
    public const string Standard = "standard";
    public const string Express = "express";
}

/// <summary>A Plaid Item (bank or brokerage login) a shopper has connected.</summary>
public class BankConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string Kind { get; set; } = BankConnectionKinds.Bank;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Plaid access token, protected with ASP.NET Core data protection. Never returned by the API.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>The account used to pay (bank connections).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    /// <summary>Cursor of the last /transactions/sync applied to the local copy.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The shop's own copy of a transaction on a connected bank.</summary>
public class BankTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
}

public static class CheckoutStatuses
{
    public const string Open = "open";
    public const string Confirming = "confirming";
    public const string Completed = "completed";
}

public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public decimal Shipping { get; set; }
    public string Status { get; set; } = CheckoutStatuses.Open;
    public string? ShipStreet { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipZip { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Concurrency token: change it on every update.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal ItemsTotal()
    {
        var total = 0m;
        foreach (var item in Items) total += item.UnitPrice * item.Quantity;
        return total;
    }

    public decimal Total() => ItemsTotal() + Shipping;
}

public class BankCheckoutItem
{
    public int Id { get; set; }
    public Guid CheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public static class PaymentStatuses
{
    public const string Paid = "paid";
    public const string Cancelling = "cancelling";
    public const string Cancelled = "cancelled";
}

/// <summary>The bank payment behind an order.</summary>
public class BankPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OrderId { get; set; }
    public Guid CheckoutId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Speed { get; set; } = PaymentSpeeds.Standard;
    public decimal OrderTotal { get; set; }
    public decimal Fee { get; set; }
    /// <summary>Order total plus fee: everything the shopper was debited.</summary>
    public decimal Charged { get; set; }
    public DateTime ArrivesOn { get; set; }
    public string Status { get; set; } = PaymentStatuses.Paid;
    public DateTimeOffset? ShippedOn { get; set; }
    public DateTimeOffset? CancelledOn { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Concurrency token: change it on every update.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<BankPaymentTransfer> Transfers { get; set; } = new();
}

/// <summary>One Plaid transfer. Orders above the network limit are charged as several transfers.</summary>
public class BankPaymentTransfer
{
    public int Id { get; set; }
    public Guid PaymentId { get; set; }
    public string PlaidTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    /// <summary>True once the money is cancelled or refunded.</summary>
    public bool Reversed { get; set; }
}
