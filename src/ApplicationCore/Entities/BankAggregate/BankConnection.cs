using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

public enum BankConnectionKind
{
    /// <summary>A bank account that can be paid from and whose transactions can be looked up.</summary>
    Bank = 0,
    /// <summary>A brokerage account whose holdings feed the portfolio page.</summary>
    Brokerage = 1
}

/// <summary>
/// A shopper's Plaid Item (one linked bank or brokerage login).
/// </summary>
public class BankConnection : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper, as named by the JWT.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Upper-cased <see cref="UserName"/>, used for case-insensitive lookups.</summary>
    public string UserKey { get; set; } = string.Empty;

    public BankConnectionKind Kind { get; set; }
    public string PlaidItemId { get; set; } = string.Empty;

    /// <summary>The Plaid access token, protected with ASP.NET Core data protection.</summary>
    public string ProtectedAccessToken { get; set; } = string.Empty;

    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>The account payments are drawn from (bank connections only).</summary>
    public string? PaymentAccountId { get; set; }
    public string? PaymentAccountName { get; set; }
    public string? PaymentAccountMask { get; set; }

    /// <summary>The Plaid transactions-sync cursor of the local transaction copy.</summary>
    public string? TransactionsCursor { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
