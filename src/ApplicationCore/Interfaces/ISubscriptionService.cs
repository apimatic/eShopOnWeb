using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<Subscription> SubscribeAsync(string userId, string productHandle, CancellationToken cancellationToken = default);

    Task<Subscription?> GetMySubscriptionAsync(string userId, CancellationToken cancellationToken = default);

    Task RecordOrderPlacedUsageAsync(string userId, CancellationToken cancellationToken = default);

    Task<UsageResult> RecordUsageAsync(string userId, bool isAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, bool isAdmin, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<Subscription> CommitPlanChangeAsync(string userId, bool isAdmin, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<Subscription> PauseAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelAsync(string userId, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);
}
