using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Identity;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity> FromHttpContextAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(StatusCodes.Status401Unauthorized,
                "The caller's identity is missing from the token.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingException(StatusCodes.Status401Unauthorized,
                "The authenticated user could not be found.");
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BillingException(StatusCodes.Status400BadRequest,
                "The authenticated user does not have an email address required for billing.");
        }

        var (firstName, lastName) = SplitName(user);
        return new ShopperIdentity(user.Id, user.Email, firstName, lastName);
    }

    internal static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var source = user.UserName ?? user.Email ?? "Shopper";
        var at = source.IndexOf('@');
        var local = at >= 0 ? source[..at] : source;
        var parts = local.Split(new[] { '.', '_', '-' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(local), "Shopper");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shopper";
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
