using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider's acknowledgement that metered usage was recorded against a subscription.
/// </summary>
public class UsageRecord
{
    public UsageRecord(long id, int subscriptionId, string componentHandle, decimal quantity)
    {
        Guard.Against.NullOrEmpty(componentHandle, nameof(componentHandle));

        Id = id;
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
    }

    public long Id { get; }

    public int SubscriptionId { get; }

    public string ComponentHandle { get; }

    /// <summary>The quantity recorded by this single call, not the running total.</summary>
    public decimal Quantity { get; }

    public string? Memo { get; init; }

    /// <summary>
    /// The period-to-date running total after this call, or <c>null</c> when the read-back
    /// failed. A failed read-back never fails the recording itself (UC2).
    /// </summary>
    public decimal? PeriodToDateTotal { get; init; }
}
