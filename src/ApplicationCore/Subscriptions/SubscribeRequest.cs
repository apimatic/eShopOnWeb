namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to enroll an eShopOnWeb user in a plan. The <see cref="UserReference"/> is the stable
/// identity of the caller (their username/email from the JWT) and is used as the Maxio customer
/// reference so that the customer↔subscription relationship is idempotent.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(string userReference, string email, string firstName, string lastName, string planHandle)
    {
        UserReference = userReference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PlanHandle = planHandle;
    }

    /// <summary>Stable unique identifier of the eShopOnWeb user; used as the Maxio customer reference.</summary>
    public string UserReference { get; }

    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>The API handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }
}
