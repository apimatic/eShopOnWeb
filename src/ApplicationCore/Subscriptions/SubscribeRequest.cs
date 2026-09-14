namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command to enroll an eShop user in a plan. The <see cref="CustomerReference"/> is the
/// stable idempotency key (the user's identity from the JWT), so repeated submits resolve to
/// the same billing customer rather than creating duplicates.
/// </summary>
public class SubscribeRequest
{
    /// <summary>Stable customer reference — the eShop username/email from the caller's token.</summary>
    public string CustomerReference { get; init; } = string.Empty;

    /// <summary>Customer email address (required by the billing system to create a customer).</summary>
    public string Email { get; init; } = string.Empty;

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; init; } = string.Empty;
}
