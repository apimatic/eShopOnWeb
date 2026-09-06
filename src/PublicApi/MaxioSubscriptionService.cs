using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default);
    Task<int?> EnsureCustomerExistsAsync(string userId, string email, string? firstName = null, string? lastName = null, CancellationToken ct = default);
    Task<SubscriptionDetail?> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDetail>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDetail
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public int? CustomerId { get; set; }
    public decimal? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(MaxioSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        // Mock implementation for sandbox testing
        await Task.Delay(10, ct);

        return new List<SubscriptionPlan>
        {
            new SubscriptionPlan
            {
                Id = 7126957,
                Handle = "eshop-pro",
                Name = "Pro Plan",
                PriceInCents = 29900m,
                Interval = 1,
                IntervalUnit = "month"
            },
            new SubscriptionPlan
            {
                Id = 7126958,
                Handle = "basic-plan",
                Name = "Basic Plan",
                PriceInCents = 2900m,
                Interval = 1,
                IntervalUnit = "month"
            }
        };
    }

    public async Task<int?> EnsureCustomerExistsAsync(string userId, string email, string? firstName = null, string? lastName = null, CancellationToken ct = default)
    {
        // Mock implementation: return a fake customer ID
        await Task.Delay(10, ct);
        return userId.GetHashCode() % int.MaxValue;
    }

    public async Task<SubscriptionDetail?> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        // Mock implementation
        await Task.Delay(50, ct);

        return new SubscriptionDetail
        {
            Id = 12345,
            State = "active",
            CustomerId = customerId,
            ProductHandle = productHandle,
            ProductName = productHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
            ProductPriceInCents = productHandle == "eshop-pro" ? 29900m : 2900m,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<List<SubscriptionDetail>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        // Mock implementation
        await Task.Delay(20, ct);

        return new List<SubscriptionDetail>
        {
            new SubscriptionDetail
            {
                Id = 12345,
                State = "active",
                CustomerId = customerId,
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900m,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1)
            }
        };
    }
}
