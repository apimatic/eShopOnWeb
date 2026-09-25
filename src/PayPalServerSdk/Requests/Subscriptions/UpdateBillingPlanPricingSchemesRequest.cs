using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the UpdateBillingPlanPricingSchemes operation.
/// </summary>
public sealed record UpdateBillingPlanPricingSchemesRequest
{
    /// <summary>
    /// The ID for the plan.
    /// </summary>
    public required string Id { get; init; }

    public UpdatePricingSchemesRequest? Body { get; init; }
}
