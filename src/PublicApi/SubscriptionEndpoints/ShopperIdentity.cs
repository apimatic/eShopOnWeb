using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<(ApplicationUser? User, IResult? Failure)> GetRequiredUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name) || principal.Identity?.IsAuthenticated != true)
        {
            return (null, Results.Unauthorized());
        }

        var user = await userManager.FindByNameAsync(name);
        if (user is null)
        {
            return (null, Results.Unauthorized());
        }

        return (user, null);
    }

    public static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var source = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("eShop", "Customer");
        }

        var local = source;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            local = source[..at];
        }

        var parts = local.Split(new[] { '.', ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    public static string RequireEmail(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName) && user.UserName.Contains('@'))
        {
            return user.UserName;
        }

        throw new InvalidOperationException("The authenticated user does not have an email address, which Maxio requires to create a customer.");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
