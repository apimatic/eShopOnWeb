using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionIdentity
{
    public static SubscriptionSubscriber CreateFromUsername(string username)
    {
        var firstName = username;
        var lastName = "User";
        var atIndex = username.IndexOf('@');
        if (atIndex > 0)
        {
            var local = username.Substring(0, atIndex);
            var domain = username.Substring(atIndex + 1);
            firstName = SplitFirst(local);
            lastName = SplitFirst(domain);
        }

        return new SubscriptionSubscriber($"eshop:{username}", username, firstName, lastName);
    }

    private static string SplitFirst(string value)
    {
        var separator = value.IndexOf('.');
        return separator > 0 ? value.Substring(0, separator) : value;
    }
}
