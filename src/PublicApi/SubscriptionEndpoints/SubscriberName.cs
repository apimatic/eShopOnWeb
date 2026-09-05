namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// eShopOnWeb's ApplicationUser has no first/last name fields, but Maxio requires both to
/// create a customer. Derive a reasonable pair from the email address the user signed up with.
/// </summary>
internal static class SubscriberName
{
    public static (string FirstName, string LastName) FromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        return (localPart, "eShopOnWeb Customer");
    }
}
