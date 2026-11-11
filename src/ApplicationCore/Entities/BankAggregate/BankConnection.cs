using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankConnectionKinds
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

/// <summary>
/// A Plaid Item a shopper connected through Plaid Link. Persisted so the shopper can use it on a later visit.
/// </summary>
public class BankConnection : BaseEntity
{
    /// <summary>The Identity user id of the owner.</summary>
    public string UserId { get; set; } = string.Empty;
    public string Kind { get; set; } = BankConnectionKinds.Bank;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Secret. Never returned from the API.</summary>
    public string AccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>The account the shopper selected in Link (required for banks, optional for brokerages).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    /// <summary>Transactions sync cursor of the local copy of the bank activity.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<BankTransaction> Transactions { get; set; } = new();
}
