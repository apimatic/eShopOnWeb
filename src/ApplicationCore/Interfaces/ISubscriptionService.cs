using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int IntervalDays { get; set; }
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default);
    Task<SubscriptionInfo> CreateSubscriptionAsync(string userId, string userEmail, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}
