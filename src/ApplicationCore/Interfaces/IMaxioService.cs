using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioPlan[]> GetSubscriptionPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> GetOrCreateMaxioCustomerAsync(string userId, string email, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken cancellationToken = default);
    Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}

public class MaxioPlan
{
    public long Id { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    public string? Description { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string Reference { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public int? PlanId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public decimal? Price { get; set; }
    public string State { get; set; } = null!;
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
