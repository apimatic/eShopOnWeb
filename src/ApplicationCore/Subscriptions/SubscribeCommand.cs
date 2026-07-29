namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The intent to enroll an eShopOnWeb user into a subscription plan. The user
/// identity fields are resolved server-side from the caller's token — they are
/// never accepted from the request body.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(string userReference, string email, string firstName, string lastName, string planHandle)
    {
        UserReference = userReference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PlanHandle = planHandle;
    }

    /// <summary>
    /// Stable, unique identifier of the eShopOnWeb user. Used as the Maxio
    /// customer <c>reference</c> so the mapping between an app user and a Maxio
    /// customer is idempotent.
    /// </summary>
    public string UserReference { get; }

    /// <summary>The user's email address (used when creating the Maxio customer).</summary>
    public string Email { get; }

    /// <summary>First name for the Maxio customer record.</summary>
    public string FirstName { get; }

    /// <summary>Last name for the Maxio customer record.</summary>
    public string LastName { get; }

    /// <summary>Handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }
}
