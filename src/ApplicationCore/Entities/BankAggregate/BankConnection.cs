using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankConnectionTypes
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

/// <summary>
/// A Plaid Item a shopper connected through Plaid Link: a bank account used to pay by bank,
/// or a brokerage account shown on the portfolio page.
/// </summary>
public class BankConnection : BaseEntity
{
    public string BuyerId { get; set; } = string.Empty;
    public string Type { get; set; } = BankConnectionTypes.Bank;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Plaid access token. Never returned by the API and never logged.</summary>
    public string AccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    /// <summary>The account payments are drawn from (bank connections only).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
    /// <summary>Cursor of the last /transactions/sync applied to the local copy.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
