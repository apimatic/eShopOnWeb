using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Application-facing facade over the Maxio Advanced Billing SDK. All Maxio
/// interaction flows through this service so SDK errors are translated in exactly
/// one place. It is stateless with respect to a single request and may be shared.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscribe-able plans in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for <see cref="SubscribeToPlanRequest.CustomerReference"/>
    /// and subscribes them to the requested plan. When the customer is already subscribed
    /// to the plan the existing subscription is returned with <see cref="SubscribeToPlanResult.Created"/>
    /// set to false.
    /// </summary>
    Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken ct);

    /// <summary>
    /// Lists every Maxio subscription for the customer identified by
    /// <paramref name="customerReference"/>. Returns an empty list when no Maxio
    /// customer exists yet (no side effects).
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken ct);
}

public class SubscribeToPlanRequest
{
    /// <summary>Deterministic Maxio customer reference for the eShopOnWeb user.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Contact email stored on the Maxio customer.</summary>
    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>Handle of the plan to subscribe to (must be a plan in the configured family).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeToPlanResult
{
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when this call created the subscription; false when an existing one was returned.</summary>
    public bool Created { get; set; }
}
