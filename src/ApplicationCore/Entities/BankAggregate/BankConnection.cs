using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankConnectionKind
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>
/// A shopper's Plaid Item (a login at a bank or brokerage). Persisted so the shopper can pay with it,
/// or see their portfolio, on a later visit.
/// </summary>
public class BankConnection : BaseEntity
{
    /// <summary>Normalised (lower-case) shopper identity, taken from the JWT.</summary>
    public string UserId { get; set; } = string.Empty;
    public BankConnectionKind Kind { get; set; }
    public string PlaidItemId { get; set; } = string.Empty;

    /// <summary>Plaid access token, protected at rest by the data-protection API.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;

    /// <summary>The checking/savings account used to pay (banks only).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>Cursor of the last completed /transactions/sync pass.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
