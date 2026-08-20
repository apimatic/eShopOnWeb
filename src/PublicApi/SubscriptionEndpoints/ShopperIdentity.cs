using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static string? From(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static (string FirstName, string LastName) SplitDisplayName(string emailOrName)
    {
        var local = emailOrName;
        var at = emailOrName.IndexOf('@');
        if (at > 0)
        {
            local = emailOrName[..at];
        }

        if (string.IsNullOrWhiteSpace(local))
        {
            local = "shopper";
        }

        return (local, "eShopOnWeb");
    }
}
