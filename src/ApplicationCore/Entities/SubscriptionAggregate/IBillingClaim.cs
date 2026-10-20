using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A row whose primary key is claimed before a billing-provider write is sent, so that a second
/// request for the same key is refused by the store instead of reaching the provider.
/// </summary>
public interface IBillingClaim
{
    BillingClaimStatus Status { get; }
    int? ProviderId { get; }
    DateTimeOffset ClaimedAt { get; }
    DateTimeOffset UpdatedAt { get; }

    /// <summary>Optimistic-concurrency token; changes on every take-over so that only one request wins it.</summary>
    Guid Version { get; }

    void TakeOver(DateTimeOffset now);
    void Complete(int providerId, DateTimeOffset now);
    void MarkUnknown(DateTimeOffset now);
}
