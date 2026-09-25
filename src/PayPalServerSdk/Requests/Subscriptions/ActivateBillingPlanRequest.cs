namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the ActivateBillingPlan operation.
/// </summary>
public sealed record ActivateBillingPlanRequest
{
    /// <summary>
    /// The ID of the plan.
    /// </summary>
    public required string Id { get; init; }
}
