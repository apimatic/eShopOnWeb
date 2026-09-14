namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionSubscriber
{
    public SubscriptionSubscriber(string reference, string email, string? firstName = null, string? lastName = null)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Reference { get; }
    public string Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
}
