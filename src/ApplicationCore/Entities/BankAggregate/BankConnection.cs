using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class ConnectionKinds
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

/// <summary>A Plaid Item a shopper linked: a bank account (to pay with) or a brokerage account.</summary>
public class BankConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = "";
    public string Kind { get; set; } = ConnectionKinds.Bank;
    public string ItemId { get; set; } = "";
    /// <summary>Cleared when the connection is disconnected.</summary>
    public string? AccessToken { get; set; }
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>The account payments are drawn from (bank connections).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    public string? SyncCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisconnectedAt { get; set; }

    public bool IsActive => DisconnectedAt == null;
    public List<BankTransaction> Transactions { get; set; } = new();
}

public class BankTransaction
{
    public int Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
    public string? PendingTransactionId { get; set; }
}

public class BankCheckoutItem
{
    public int Id { get; set; }
    public Guid CheckoutId { get; set; }
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = "";
    public string PictureUri { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public static class CheckoutStatuses
{
    public const string Open = "open";
    public const string Confirming = "confirming";
    public const string Confirmed = "confirmed";
}

public class BankCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public decimal Shipping { get; set; }
    public string Status { get; set; } = CheckoutStatuses.Open;
    public string? ShipStreet { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipZip { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankCheckoutItem> Items { get; set; } = new();

    public decimal ItemsTotal()
    {
        decimal t = 0;
        foreach (var i in Items) t = checked(t + i.UnitPrice * i.Quantity);
        return t;
    }

    public decimal Total() => checked(ItemsTotal() + Shipping);
}

public static class PaymentStatuses
{
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
}

/// <summary>The bank payment behind an order.</summary>
public class BankPayment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Guid CheckoutId { get; set; }
    public Guid ConnectionId { get; set; }
    public string BuyerId { get; set; } = "";
    public string TransferId { get; set; } = "";
    public string Speed { get; set; } = "standard";
    public decimal OrderTotal { get; set; }
    public decimal Fee { get; set; }
    /// <summary>What was debited: order total + fee.</summary>
    public decimal Charged { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public string Status { get; set; } = PaymentStatuses.Paid;
    public DateTimeOffset? ShippedAt { get; set; }
    public string? RefundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
