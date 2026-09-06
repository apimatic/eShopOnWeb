using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<GetSubscriptionPlansResponse> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);
    Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userId, string email, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default);
    Task<GetUserSubscriptionsResponse> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
    public int IntervalValue { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionResponse
{
    public long SubscriptionId { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public decimal Price { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscriptionDetailDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public decimal Price { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
}

public class GetUserSubscriptionsResponse
{
    public List<SubscriptionDetailDto> Subscriptions { get; set; } = new();
}
