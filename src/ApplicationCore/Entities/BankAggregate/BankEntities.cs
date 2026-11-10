using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>
/// A bank or brokerage Item a shopper connected through Plaid Link.
/// The access token never leaves the server.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public bool HasBankAccounts { get; set; }
    public bool HasInvestmentAccounts { get; set; }
    /// <summary>JSON array of the accounts Plaid reported for the item.</summary>
    public string AccountsJson { get; set; } = "[]";
    /// <summary>Plaid /transactions/sync cursor for the local copy.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The shop's own copy of a transaction reported by the bank (pending ones included).</summary>
public class BankTransaction : BaseEntity, IAggregateRoot
{
    public int ConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
}

public static class BankCheckoutStatus
{
    public const string Open = "open";
    public const string Confirmed = "confirmed";
}

/// <summary>A pay-by-bank checkout that has been started but maybe not yet confirmed.</summary>
public class BankCheckout : IAggregateRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    /// <summary>JSON array of the priced lines.</summary>
    public string ItemsJson { get; set; } = "[]";
    public string ShipToJson { get; set; } = "{}";
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public string Status { get; set; } = BankCheckoutStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? OrderId { get; set; }
    public string? Speed { get; set; }
    public decimal? Fee { get; set; }
    public DateTime? ArrivesOn { get; set; }
}

/// <summary>The money movement behind an order paid by bank.</summary>
public class BankPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string TransferId { get; set; } = string.Empty;
    /// <summary>Everything the shopper was charged: goods, shipping and fee.</summary>
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string Speed { get; set; } = string.Empty;
    public DateTime ArrivesOn { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
