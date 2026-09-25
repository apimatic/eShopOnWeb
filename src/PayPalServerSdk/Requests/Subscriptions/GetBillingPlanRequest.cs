namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the GetBillingPlan operation.
/// </summary>
public sealed record GetBillingPlanRequest
{
    /// <summary>
    /// The ID of the plan.
    /// </summary>
    public required string Id { get; init; }
}
