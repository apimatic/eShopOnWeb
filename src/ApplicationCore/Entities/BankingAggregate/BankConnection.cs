using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

public enum BankConnectionKind
{
    Bank = 0,
    Brokerage = 1
}

/// <summary>
/// A bank or brokerage account a shopper connected through Plaid Link.
/// The shopper is identified by the (normalised) e-mail address carried by the JWT.
/// </summary>
public class BankConnection : BaseEntity
{
    public string OwnerId { get; set; } = string.Empty;
    public BankConnectionKind Kind { get; set; }
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Plaid access token, protected with ASP.NET Core data protection. Cleared on disconnect.</summary>
    public string? ProtectedAccessToken { get; set; }

    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>The account used to pay (banks); null for brokerage connections.</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }

    /// <summary>Cursor of /transactions/sync, so the local copy resumes where it stopped.</summary>
    public string? SyncCursor { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisconnectedAt { get; set; }
}
