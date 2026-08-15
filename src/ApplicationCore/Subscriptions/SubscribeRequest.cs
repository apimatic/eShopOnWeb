namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Everything the billing layer needs to enroll a shopper in a plan. <see cref="UserReference"/>
/// is the stable, per-user idempotency key: the billing customer is keyed on it so a repeated
/// subscribe never creates a second customer.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(string userReference, string email, string planHandle, string? firstName = null, string? lastName = null)
    {
        UserReference = userReference;
        Email = email;
        PlanHandle = planHandle;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable eShopOnWeb user identifier used as the billing customer reference (idempotency key).</summary>
    public string UserReference { get; }

    /// <summary>Shopper email, recorded on the billing customer.</summary>
    public string Email { get; }

    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; }

    /// <summary>Optional first name; derived from the email local-part when omitted.</summary>
    public string? FirstName { get; }

    /// <summary>Optional last name; a neutral default is used when omitted.</summary>
    public string? LastName { get; }
}
