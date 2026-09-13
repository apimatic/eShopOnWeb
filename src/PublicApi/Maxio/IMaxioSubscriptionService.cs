using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Service for interacting with Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists all active products (plans) in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync();

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user (idempotent),
    /// then creates a subscription for the specified product handle.
    /// </summary>
    Task<CreateSubscriptionResult> CreateSubscriptionAsync(
        string userEmail,
        string firstName,
        string lastName,
        string productHandle,
        string? customerReference = null);

    /// <summary>
    /// Lists all subscriptions for the customer identified by the given reference.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference);
}
