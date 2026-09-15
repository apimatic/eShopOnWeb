using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionEnrollment?> SubscribeAsync(Shopper shopper, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken);
}

public sealed record Shopper(string Id, string Email);

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    long Id,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string State,
    System.DateTimeOffset? NextBillingAt);

public sealed record SubscriptionEnrollment(SubscriptionDto Subscription, bool Created);
