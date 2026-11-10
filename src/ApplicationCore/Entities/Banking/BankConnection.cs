using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Banking;

public enum ConnectionPurpose
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>
/// A Plaid Item a shopper connected through Plaid Link. Saved so it can be used on later visits.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; set; } = string.Empty;
    public ConnectionPurpose Purpose { get; set; }
    public string PlaidItemId { get; set; } = string.Empty;
    /// <summary>The Plaid access token, protected with ASP.NET Core data protection.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
