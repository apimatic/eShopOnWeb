using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as reported by the Maxio Advanced Billing API.
/// </summary>
public class MaxioSubscriptionInfo
{
    public MaxioSubscriptionInfo(int id,
        int customerId,
        int productId,
        string? productHandle,
        string? productName,
        int priceInCents,
        string state,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        CustomerId = customerId;
        ProductId = productId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CreatedAt = createdAt;
    }

    public int Id { get; }
    public int CustomerId { get; }
    public int ProductId { get; }
    public string? ProductHandle { get; }
    public string? ProductName { get; }
    public int PriceInCents { get; }
    public string State { get; }
    public DateTimeOffset? NextBillingAt { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// States that are still consuming a seat in the plan (i.e. not canceled/expired).
    /// </summary>
    public bool IsLive =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase);

    public decimal Price => PriceInCents / 100m;
}
