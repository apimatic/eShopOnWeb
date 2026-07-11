using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageRecordDto
{
    public long Id { get; init; }
    public string ComponentHandle { get; init; } = string.Empty;
    public double Quantity { get; init; }
    public string? Memo { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }

    public static UsageRecordDto FromDomain(UsageRecord usage) => new()
    {
        Id = usage.Id,
        ComponentHandle = usage.ComponentHandle,
        Quantity = usage.Quantity,
        Memo = usage.Memo,
        RecordedAt = usage.RecordedAt,
    };
}
