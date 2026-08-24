using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a user to a plan. Idempotent: the billing customer is looked up by the
    /// eShopOnWeb user id (created only when absent) and a repeated subscribe for the same
    /// user + plan returns the existing subscription instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions; empty when the user has no billing customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}

public class SubscribeCommand
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}
