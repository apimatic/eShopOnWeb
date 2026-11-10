using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Data;

public class BankConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The shopper (JWT name), lower-cased.</summary>
    public string UserName { get; set; } = "";
    public string ItemId { get; set; } = "";

    /// <summary>The Plaid access token, protected with ASP.NET Data Protection. Never returned by the API.</summary>
    public string ProtectedAccessToken { get; set; } = "";
    public string? InstitutionId { get; set; }
    public string InstitutionName { get; set; } = "";

    /// <summary>Comma separated Plaid products the item was linked with.</summary>
    public string Products { get; set; } = "";
    public bool IsBank { get; set; }
    public bool IsBrokerage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Cursor of the last completed transactions sync.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public List<BankAccount> Accounts { get; set; } = new();
}

public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public string PlaidAccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Mask { get; set; }
    public string Type { get; set; } = "";
    public string? Subtype { get; set; }
}

/// <summary>The shop's own copy of a connected bank's transactions.</summary>
public class BankTransaction
{
    public Guid ConnectionId { get; set; }
    public string TransactionId { get; set; } = "";
    public string PlaidAccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
}

public static class BankPaymentStatus
{
    public const string Started = "started";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
}

/// <summary>A checkout paid by bank. Becomes the record of the order's payment once confirmed.</summary>
public class BankPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public string PlaidAccountId { get; set; } = "";
    public string ItemsJson { get; set; } = "[]";
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public string? ShipToJson { get; set; }
    public string Status { get; set; } = BankPaymentStatus.Started;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Bumped on every change; guards concurrent updates and keys Plaid idempotency.</summary>
    public int Revision { get; set; }

    public string? Speed { get; set; }
    public decimal Fee { get; set; }
    public string? AuthorizationId { get; set; }
    public string? TransferId { get; set; }
    public DateTime? ArrivesOn { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RefundId { get; set; }
}
