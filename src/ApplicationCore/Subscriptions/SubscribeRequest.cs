namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Everything the billing service needs to enroll a shopper. Identity fields come from the
/// authenticated caller (JWT), never from the request body.
/// </summary>
public record SubscribeRequest
{
    /// <summary>Stable eShop user identifier (the username/email). Used as the Maxio customer reference.</summary>
    public required string UserReference { get; init; }

    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }

    /// <summary>The handle of the plan to subscribe to. Must be one of the live plan handles.</summary>
    public required string PlanHandle { get; init; }
}
