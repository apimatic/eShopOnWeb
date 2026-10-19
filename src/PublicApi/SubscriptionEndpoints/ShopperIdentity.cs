using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<ShopperProfile> RequireAsync(HttpContext httpContext)
    {
        var users = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var name = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw new SubscriptionBillingException("The caller is not signed in.", StatusCodes.Status401Unauthorized);

        var user = await users.FindByNameAsync(name);
        if (user == null || string.IsNullOrWhiteSpace(user.Id))
            throw new SubscriptionBillingException("The caller is not signed in.", StatusCodes.Status401Unauthorized);

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        if (string.IsNullOrWhiteSpace(email))
            throw new SubscriptionBillingException("The caller is not signed in.", StatusCodes.Status401Unauthorized);

        var (firstName, lastName) = SplitName(email);
        return new ShopperProfile
        {
            UserId = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (Title(parts[0]), Title(string.Join(" ", parts.Skip(1))));
        if (parts.Length == 1)
            return (Title(parts[0]), "Subscriber");
        return ("Shopper", "Subscriber");
    }

    private static string Title(string value)
    {
        if (value.Length == 1)
            return value.ToUpperInvariant();
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
