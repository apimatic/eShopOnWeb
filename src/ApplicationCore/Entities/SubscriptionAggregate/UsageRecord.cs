using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A single accepted usage report against a metered component on a subscription.</summary>
public sealed record UsageRecord(
    long Id,
    decimal Quantity,
    string? Memo,
    DateTimeOffset? RecordedAt,
    int? ComponentId,
    string? ComponentHandle);
