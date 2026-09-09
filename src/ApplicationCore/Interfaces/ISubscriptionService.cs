using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Command to enroll a shopper in a recurring plan backed by Maxio.
/// </summary>
public sealed record SubscribeCommand(
    string UserId,
    string UserName,
    string? Email,
    string ProductHandle);

public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscribable plans (Maxio products) of the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enrolls the user in the plan identified by <see cref="SubscribeCommand.ProductHandle"/>.
    /// Idempotent: a repeated call (double-click) returns the existing Maxio subscription
    /// instead of creating a second one. Returns null when the plan does not exist.
    /// </summary>
    Task<SubscriptionDetails?> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the caller's subscriptions with their live Maxio state
    /// (plan, price, state, next billing date).
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
