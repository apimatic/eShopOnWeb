using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanInfo>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionInfo> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default);
    Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public class SubscriptionPlanInfo
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; }
    public int? ProductFamilyId { get; set; }
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
