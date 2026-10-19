using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public enum MaxioClaimStatus
{
    /// <summary>Claimed by a request that has not finished talking to Maxio yet.</summary>
    Pending,
    /// <summary>Maxio confirmed the record; its id is stored.</summary>
    Created,
    /// <summary>A write was sent but its outcome could not be confirmed; settle by re-reading Maxio before any new write.</summary>
    Unknown
}

/// <summary>
/// The claim that makes "ensure a Maxio customer exists" idempotent: one row per customer reference,
/// inserted before any customer write so a concurrent second request is refused by the primary key.
/// </summary>
public class MaxioCustomerClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerClaim() { }
    #pragma warning restore CS8618

    public MaxioCustomerClaim(string reference, DateTimeOffset now)
    {
        Reference = reference;
        Status = MaxioClaimStatus.Pending;
        ClaimedAt = now;
        Touch(now);
    }

    /// <summary>The Maxio customer reference (the eShopOnWeb user name). Primary key.</summary>
    public string Reference { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public MaxioClaimStatus Status { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Optimistic-concurrency token: two requests taking over the same stale claim cannot both win.</summary>
    public Guid Version { get; private set; }

    public void MarkCreated(int maxioCustomerId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        Status = MaxioClaimStatus.Created;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = MaxioClaimStatus.Unknown;
        Touch(now);
    }

    public void TakeOver(DateTimeOffset now)
    {
        Status = MaxioClaimStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

/// <summary>
/// The claim that makes subscribe idempotent: one row per eShopOnWeb user, inserted before
/// <c>CreateSubscription</c> so a double-click is refused by the primary key instead of reaching Maxio twice.
/// </summary>
public class MaxioSubscriptionClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscriptionClaim() { }
    #pragma warning restore CS8618

    public MaxioSubscriptionClaim(string userName, string productHandle, int maxioCustomerId, DateTimeOffset now)
    {
        UserName = userName;
        ClaimedAt = now;
        Begin(productHandle, maxioCustomerId, now);
    }

    /// <summary>The eShopOnWeb user name. Primary key: one subscription per user in the configured family.</summary>
    public string UserName { get; private set; }
    /// <summary>App-generated reference sent as the Maxio subscription <c>reference</c>; used to find the write if its outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }
    public string ProductHandle { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public MaxioClaimStatus Status { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    /// <summary>Starts a fresh subscribe attempt on this claim, with a new subscription reference.</summary>
    [MemberNotNull(nameof(SubscriptionReference), nameof(ProductHandle))]
    public void Begin(string productHandle, int maxioCustomerId, DateTimeOffset now)
    {
        SubscriptionReference = $"eshop-{Guid.NewGuid():N}";
        ProductHandle = productHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = null;
        Status = MaxioClaimStatus.Pending;
        Touch(now);
    }

    /// <summary>Retries the same attempt (same reference) after Maxio confirmed the earlier send did not land.</summary>
    public void TakeOver(DateTimeOffset now)
    {
        Status = MaxioClaimStatus.Pending;
        Touch(now);
    }

    public void MarkCreated(int maxioSubscriptionId, string productHandle, DateTimeOffset now)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        Status = MaxioClaimStatus.Created;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = MaxioClaimStatus.Unknown;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
