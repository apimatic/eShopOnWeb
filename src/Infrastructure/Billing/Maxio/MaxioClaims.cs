using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public enum MaxioClaimState
{
    /// <summary>Claimed; the provider write is in flight.</summary>
    Pending = 0,

    /// <summary>The provider confirmed the write; the provider id is recorded.</summary>
    Created = 1,

    /// <summary>The provider may have acted but did not confirm; re-checked before anything else happens for this user.</summary>
    Unknown = 2
}

/// <summary>
/// One row per eShop user who has (or is being given) a Maxio customer. The primary key is the user
/// name, so a second concurrent claim for the same user is refused by the store.
/// </summary>
public class MaxioCustomerClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerClaim() { }

    public MaxioCustomerClaim(string userName, DateTimeOffset now)
    {
        UserName = userName;
        State = MaxioClaimState.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserName { get; private set; }
    public MaxioClaimState State { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCreated(int maxioCustomerId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        State = MaxioClaimState.Created;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        State = MaxioClaimState.Unknown;
        UpdatedAt = now;
    }

    public void Retake(DateTimeOffset now)
    {
        State = MaxioClaimState.Pending;
        UpdatedAt = now;
    }
}

/// <summary>
/// One row per eShop user holding (or being enrolled in) a subscription. The primary key is the user
/// name, so a double-submit is refused by the store before a second request reaches the provider.
/// </summary>
public class MaxioSubscriptionClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscriptionClaim() { }

    public MaxioSubscriptionClaim(string userName, string planHandle, string reference, DateTimeOffset now)
    {
        UserName = userName;
        PlanHandle = planHandle;
        Reference = reference;
        State = MaxioClaimState.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserName { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The subscription <c>reference</c> sent to Maxio; the handle used to find the write again.</summary>
    public string Reference { get; private set; }

    public MaxioClaimState State { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCreated(int maxioSubscriptionId, string planHandle, string? reference, DateTimeOffset now)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        if (!string.IsNullOrEmpty(reference))
        {
            Reference = reference;
        }
        State = MaxioClaimState.Created;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        State = MaxioClaimState.Unknown;
        UpdatedAt = now;
    }
}
