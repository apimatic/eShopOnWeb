using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

/// <summary>
/// A bank or brokerage Item a shopper connected through Plaid Link.
/// The access token never leaves the server.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BankConnection() { }
#pragma warning restore CS8618

    public BankConnection(string userName, string itemId, string accessToken, string? institutionId,
        string? institutionName, string? paymentAccountId, bool hasBankAccounts, bool hasInvestmentAccounts)
    {
        UserName = userName;
        ItemId = itemId;
        AccessToken = accessToken;
        InstitutionId = institutionId;
        InstitutionName = institutionName;
        PaymentAccountId = paymentAccountId;
        HasBankAccounts = hasBankAccounts;
        HasInvestmentAccounts = hasInvestmentAccounts;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserName { get; private set; }
    public string ItemId { get; private set; }
    public string AccessToken { get; private set; }
    public string? InstitutionId { get; private set; }
    public string? InstitutionName { get; private set; }

    /// <summary>The Plaid account debited when this connection pays for an order.</summary>
    public string? PaymentAccountId { get; private set; }
    public bool HasBankAccounts { get; private set; }
    public bool HasInvestmentAccounts { get; private set; }

    /// <summary>Plaid /transactions/sync cursor: everything up to it is already in BankTransactions.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
