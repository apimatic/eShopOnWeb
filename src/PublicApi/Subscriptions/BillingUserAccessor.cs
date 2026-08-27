using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IBillingUserAccessor
{
    Task<BillingUser> GetRequiredAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class BillingUserAccessor(UserManager<ApplicationUser> userManager) : IBillingUserAccessor
{
    public async Task<BillingUser> GetRequiredAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioIntegrationException(401, "authentication_required", "Authentication is required.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new MaxioIntegrationException(401, "user_not_found", "The authenticated user no longer exists.");
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new MaxioIntegrationException(422, "billing_profile_incomplete", "The account needs an email address before subscribing.");
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(ToDisplayName)
            .ToArray();

        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Skip(1).FirstOrDefault() ?? "Customer";
        return new BillingUser(user.Id, email, firstName, lastName);
    }

    private static string ToDisplayName(string value) =>
        value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
