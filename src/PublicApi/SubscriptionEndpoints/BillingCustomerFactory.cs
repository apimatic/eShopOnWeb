using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingCustomerFactory
{
    public static async Task<BillingCustomer> FromCurrentUserAsync(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name
                       ?? principal?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException("The caller's identity is missing from the access token.", HttpStatusCode.Unauthorized);
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new UserNotFoundException(userName);
        }

        var email = user.Email ?? user.UserName ?? userName;
        var (firstName, lastName) = SplitName(email);
        return new BillingCustomer(user.Id, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) SplitName(string emailOrUserName)
    {
        var local = emailOrUserName;
        var at = local.IndexOf('@');
        if (at > 0)
        {
            local = local.Substring(0, at);
        }

        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
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

        var trimmed = value.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToUpperInvariant();
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }
}
