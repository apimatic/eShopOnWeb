using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Reports consumption of a metered component against a subscription (UC2 step 1).
/// </summary>
public sealed class RecordUsageRequest
{
    public RecordUsageRequest(long subscriptionId, long componentId, int quantity, string? memo = null)
    {
        SubscriptionId = Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        ComponentId = Guard.Against.NegativeOrZero(componentId, nameof(componentId));
        Quantity = Guard.Against.NegativeOrZero(quantity, nameof(quantity));
        Memo = memo;
    }

    public long SubscriptionId { get; }

    public long ComponentId { get; }

    /// <summary>Units consumed. Zero and negative quantities are rejected before any provider call.</summary>
    public int Quantity { get; }

    public string? Memo { get; }
}
