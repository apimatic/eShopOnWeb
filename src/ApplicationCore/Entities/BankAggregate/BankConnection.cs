using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum ConnectionKind
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>
/// A shopper's Plaid item (a linked bank or brokerage login). The Plaid access token is stored protected.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    /// <summary>Normalized (lower-case) identity of the owner, taken from the JWT.</summary>
    public string ShopperId { get; set; } = string.Empty;
    public ConnectionKind Kind { get; set; }
    public string PlaidItemId { get; set; } = string.Empty;
    public string AccessTokenProtected { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>JSON array of the accounts known at connection time.</summary>
    public string AccountsJson { get; set; } = "[]";
    /// <summary>Plaid /transactions/sync cursor of our local copy.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
