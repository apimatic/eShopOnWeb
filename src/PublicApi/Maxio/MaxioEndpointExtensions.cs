using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Conversions between the authenticated eShopOnWeb user / billing failures
/// and the shapes the PublicApi endpoints return.
/// </summary>
public static class MaxioEndpointExtensions
{
    public static async Task<ApplicationUser?> ResolveApplicationUserAsync(this ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }

    public static MaxioUserInfo ToMaxioUserInfo(this ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var (firstName, lastName) = DeriveNames(email);
        return new MaxioUserInfo(user.Id, email, firstName, lastName);
    }

    public static ActionResult ToActionResult(this MaxioBillingException exception)
    {
        return new ObjectResult(new ProblemDetails
        {
            Title = "Subscription billing error",
            Detail = exception.Message,
            Status = exception.RecommendedHttpStatusCode
        })
        {
            StatusCode = exception.RecommendedHttpStatusCode
        };
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var separators = new[] { '.', '_', '-' };
        var parts = localPart.Split(separators, 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }
        return ("eShop", string.IsNullOrEmpty(localPart) ? "Customer" : Capitalize(localPart));
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
