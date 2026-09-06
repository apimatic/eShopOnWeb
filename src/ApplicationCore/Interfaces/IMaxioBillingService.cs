using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscriptionPlanDto
{
    public required int Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required decimal PriceInCents { get; set; }
    public int? TrialDays { get; set; }
}

public class SubscriptionDto
{
    public required int Id { get; set; }
    public required int CustomerId { get; set; }
    public required int ProductId { get; set; }
    public required string ProductHandle { get; set; }
    public required string State { get; set; }
    public required DateTimeOffset? NextBillingAt { get; set; }
    public required DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? Reference { get; set; }
}

public class CustomerSubscriptionDto
{
    public required int MaxioCustomerId { get; set; }
    public required SubscriptionDto[] Subscriptions { get; set; }
}

public interface IMaxioBillingService
{
    /// <summary>
    /// List available subscription plans from the configured product family
    /// </summary>
    Task<SubscriptionPlanDto[]> ListSubscriptionPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Create a subscription for a user (idempotent via reference)
    /// </summary>
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle,
        CancellationToken ct = default);

    /// <summary>
    /// Get all subscriptions for a user
    /// </summary>
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default);
}
