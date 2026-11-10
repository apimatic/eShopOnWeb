using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public static class BankConnectionKind
{
    public const string Bank = "bank";
    public const string Brokerage = "brokerage";
}

/// <summary>
/// A bank (or brokerage) account a shopper linked through Plaid Link.
/// The Plaid access token is only ever stored protected.
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BankConnection() { }
#pragma warning restore CS8618

    public BankConnection(string userName, string kind, string itemId, string protectedAccessToken,
        string? institutionId, string? institutionName, string? accountId, string? accountName, string? accountMask)
    {
        UserName = userName;
        Kind = kind;
        ItemId = itemId;
        ProtectedAccessToken = protectedAccessToken;
        InstitutionId = institutionId;
        InstitutionName = institutionName;
        AccountId = accountId;
        AccountName = accountName;
        AccountMask = accountMask;
    }

    public string UserName { get; private set; }
    public string Kind { get; private set; }
    public string ItemId { get; private set; }
    public string ProtectedAccessToken { get; private set; }
    public string? InstitutionId { get; private set; }
    public string? InstitutionName { get; private set; }

    /// <summary>The account that pays for orders (banks only).</summary>
    public string? AccountId { get; private set; }
    public string? AccountName { get; private set; }
    public string? AccountMask { get; private set; }

    /// <summary>Plaid's /transactions/sync cursor: how far the local copy of the activity has caught up.</summary>
    public string? Cursor { get; private set; }
    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;

    public void AdvanceCursor(string cursor) => Cursor = cursor;
}
