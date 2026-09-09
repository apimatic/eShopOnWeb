namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Command to enroll an application user into a subscription plan.
/// </summary>
public sealed class SubscribeCommand
{
    public SubscribeCommand(string userReference, string email, string firstName, string lastName, string planHandle)
    {
        UserReference = userReference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PlanHandle = planHandle;
    }

    /// <summary>Stable unique application reference for the user; becomes the customer reference in Maxio.</summary>
    public string UserReference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>Handle of the Maxio product (plan) to subscribe to.</summary>
    public string PlanHandle { get; }
}
