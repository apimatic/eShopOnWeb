using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string? Description { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}
