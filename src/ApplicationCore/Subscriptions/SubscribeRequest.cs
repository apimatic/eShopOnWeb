namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to <see cref="Interfaces.ISubscriptionService.SubscribeAsync"/>. The
/// <see cref="UserReference"/> is the trusted identity of the shopper (sourced from the
/// authenticated caller, never from client-supplied data) and is used as the billing
/// customer's unique external reference to guarantee idempotency.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(string userReference, string email, string planHandle,
        string? firstName = null, string? lastName = null)
    {
        UserReference = userReference;
        Email = email;
        PlanHandle = planHandle;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique identity of the shopper (their eShopOnWeb user name / email).</summary>
    public string UserReference { get; }

    public string Email { get; }

    /// <summary>Stable handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
