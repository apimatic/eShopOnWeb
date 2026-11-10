using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class ConnectionKinds
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

/// <summary>
/// A Plaid Item a shopper connected through Plaid Link (bank or brokerage).
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public string PublicId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Identity (user name) of the owning shopper, upper-cased for lookups.</summary>
    public string OwnerKey { get; set; } = string.Empty;
    public string Kind { get; set; } = ConnectionKinds.Bank;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Plaid access token, protected with ASP.NET data protection.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>Cursor of /transactions/sync for the local copy of the activity.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankConnectionAccount> Accounts { get; set; } = new();
}

public class BankConnectionAccount : BaseEntity
{
    public int BankConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Mask { get; set; }
    public string? Type { get; set; }
    public string? Subtype { get; set; }
}

/// <summary>
/// The shop's own copy of a bank transaction, kept in step with Plaid /transactions/sync.
/// </summary>
public class BankTransaction : BaseEntity
{
    public int BankConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
    public string? PendingTransactionId { get; set; }
}
