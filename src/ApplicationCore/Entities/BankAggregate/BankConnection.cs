using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankConnectionKind
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>
/// A Plaid Item a shopper connected through Plaid Link (a bank or a brokerage account).
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>The Plaid access token, protected with ASP.NET data protection.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public BankConnectionKind Kind { get; set; }
    /// <summary>Account used to pay (a checking account when there is one).</summary>
    public string? PaymentAccountId { get; set; }
    public string? PaymentAccountName { get; set; }
    public string? PaymentAccountMask { get; set; }
    /// <summary>Cursor of the last /transactions/sync call that was fully applied.</summary>
    public string? TransactionsCursor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BankTransaction> Transactions { get; set; } = new();
}
