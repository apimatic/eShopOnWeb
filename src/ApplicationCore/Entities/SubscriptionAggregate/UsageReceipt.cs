using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Confirmation that a quantity of metered usage was recorded against a subscription (UC2).
/// </summary>
public sealed record UsageReceipt
{
    public UsageReceipt(long id, decimal quantity)
    {
        Id = id;
        Quantity = quantity;
    }

    public long Id { get; init; }

    /// <summary>The quantity the provider echoed back for this usage record.</summary>
    public decimal Quantity { get; init; }

    public string? Memo { get; init; }

    public DateTimeOffset? RecordedAt { get; init; }

    public string? ComponentHandle { get; init; }
}
