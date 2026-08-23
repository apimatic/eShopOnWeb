using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IAuthenticatedBillingUserProvider
{
    Task<BillingUser?> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class AuthenticatedBillingUserProvider : IAuthenticatedBillingUserProvider
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthenticatedBillingUserProvider(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<BillingUser?> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        var localPart = user.Email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ToDisplayName)
            .ToArray();
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "Customer";
        return new BillingUser(user.Id, user.Email, firstName, lastName);
    }

    private static string ToDisplayName(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
