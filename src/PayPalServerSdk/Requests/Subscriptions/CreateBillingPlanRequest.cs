using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the CreateBillingPlan operation.
/// </summary>
public sealed record CreateBillingPlanRequest
{
    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    public string Prefer { get; init; } = "return=minimal";

    /// <summary>
    /// The server stores keys for 72 hours.
    /// </summary>
    public string? PayPalRequestId { get; init; }

    public PlanRequest? Body { get; init; }
}
