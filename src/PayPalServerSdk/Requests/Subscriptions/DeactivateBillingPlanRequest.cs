namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the DeactivateBillingPlan operation.
/// </summary>
public sealed record DeactivateBillingPlanRequest
{
    /// <summary>
    /// The ID of the plan.
    /// </summary>
    public required string Id { get; init; }
}
