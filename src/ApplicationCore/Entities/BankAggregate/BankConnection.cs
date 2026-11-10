using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankConnectionKind
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>A shopper's Plaid Item (a linked bank or brokerage login) and the account used from it.</summary>
public class BankConnection : BaseEntity
{
    public string BuyerId { get; set; } = string.Empty;
    public BankConnectionKind Kind { get; set; }
    public string PlaidItemId { get; set; } = string.Empty;
    /// <summary>Plaid access token. Never exposed through the API or logs.</summary>
    public string AccessToken { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>/transactions/sync cursor for the shop's own copy of the activity.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
