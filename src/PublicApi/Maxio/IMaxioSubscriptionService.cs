using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userReference, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public record PlanDto
{
    public int? Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? ProductFamilyHandle { get; init; }
}

public record SubscriptionDto
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public string? ProductName { get; init; }
    public string? ProductHandle { get; init; }
    public long? ProductPriceInCents { get; init; }
    public long? BalanceInCents { get; init; }
}
