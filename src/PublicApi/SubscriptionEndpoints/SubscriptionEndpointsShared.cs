namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointsShared
{
    // eShopOnWeb's Identity user has no first/last name fields; derive a display name from the
    // email-style username so Maxio's required CreateCustomer.FirstName has a real value.
    public static string FirstNameFrom(string userName)
    {
        var at = userName.IndexOf('@');
        return at > 0 ? userName[..at] : userName;
    }
}
