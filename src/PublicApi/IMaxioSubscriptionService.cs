using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
    Task<SubscriptionSummary> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
}

public sealed record SubscriptionSummary(
    int Id,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    System.DateTimeOffset? NextBillingAt);
