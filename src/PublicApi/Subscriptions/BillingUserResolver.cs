using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IBillingUserResolver
{
    Task<BillingUserIdentity> ResolveAsync(ClaimsPrincipal principal);
}

internal sealed class BillingUserResolver(UserManager<ApplicationUser> userManager) : IBillingUserResolver
{
    public async Task<BillingUserIdentity> ResolveAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingApiException(StatusCodes.Status401Unauthorized, "An authenticated user is required.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingApiException(StatusCodes.Status401Unauthorized, "The authenticated user no longer exists.");
        }

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingApiException(StatusCodes.Status422UnprocessableEntity, "An email address is required before subscribing.");
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var firstName = Humanize(nameParts.FirstOrDefault() ?? "eShop");
        var lastName = Humanize(nameParts.Skip(1).FirstOrDefault() ?? "Customer");

        return new BillingUserIdentity(user.Id, email, firstName, lastName);
    }

    private static string Humanize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
