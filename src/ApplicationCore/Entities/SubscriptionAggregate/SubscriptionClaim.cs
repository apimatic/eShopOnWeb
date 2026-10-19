using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// One subscription per (user, plan). The row is written before the provider is called, keyed by
/// <see cref="KeyFor"/>, so a double-submit is refused by the store's primary key before it can reach
/// the provider. <see cref="Reference"/> is sent to the provider as the subscription reference and is
/// what an ambiguous create is reconciled by.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    public string Id { get; private set; }
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public string Reference { get; private set; }
    public ClaimStatus Status { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }

    /// <summary>The create call failed in a way that leaves its outcome unknown and the re-read could not settle it.</summary>
    public bool OutcomeUnknown { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }

    /// <summary>Concurrency token: changes every time the claim is (re)taken or settled.</summary>
    public Guid ClaimToken { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }

    public SubscriptionClaim(string userId, string planHandle, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Id = KeyFor(userId, planHandle);
        UserId = userId;
        PlanHandle = planHandle;
        Reference = NewReference();
        Status = ClaimStatus.Pending;
        ClaimedAt = claimedAt;
        ClaimToken = Guid.NewGuid();
    }

    public static string KeyFor(string userId, string planHandle) => $"{userId}|{planHandle}";

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) =>
        Status == ClaimStatus.Pending && now - ClaimedAt > maxAge;

    /// <summary>
    /// Takes over an abandoned or unsettled claim. The previous reference is kept: the earlier
    /// create may have landed, and the reference is how it is found again.
    /// </summary>
    public void Retake(DateTimeOffset now)
    {
        ClaimedAt = now;
        ClaimToken = Guid.NewGuid();
    }

    /// <summary>Starts a fresh attempt once the previous reference is known not to exist at the provider.</summary>
    public void RenewReference()
    {
        Reference = NewReference();
        OutcomeUnknown = false;
    }

    public void MarkOutcomeUnknown()
    {
        OutcomeUnknown = true;
        ClaimToken = Guid.NewGuid();
    }

    public void Complete(int providerSubscriptionId)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        Status = ClaimStatus.Completed;
        OutcomeUnknown = false;
        ClaimToken = Guid.NewGuid();
    }

    private static string NewReference() => $"eshop-sub-{Guid.NewGuid():N}";
}
