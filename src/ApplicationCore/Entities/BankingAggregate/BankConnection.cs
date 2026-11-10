using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

/// <summary>
/// A bank or brokerage account a shopper connected through Plaid Link.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public const string KIND_BANK = "bank";
    public const string KIND_BROKERAGE = "brokerage";

    public string BuyerId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;

    /// <summary>The Plaid access token, protected with ASP.NET Core data protection.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;

    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string Kind { get; set; } = KIND_BANK;

    /// <summary>The account used to pay by bank (null for brokerage-only connections).</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }

    public bool HasDepositoryAccounts { get; set; }
    public bool HasInvestmentAccounts { get; set; }

    /// <summary>Cursor of the shop's own copy of the bank activity (Plaid transactions sync).</summary>
    public string? TransactionsCursor { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
