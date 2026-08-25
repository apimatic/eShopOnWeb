using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class BillingCustomerFactory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingCustomerFactory(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);
    }

    public static BillingCustomer Create(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                "The authenticated user's billing profile has no email address.");
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToDisplayName)
            .ToArray();
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Length > 1 ? string.Join(' ', nameParts.Skip(1)) : "Customer";

        return new BillingCustomer(user.Id, email, firstName, lastName);
    }

    private static string ToDisplayName(string value) => value.Length switch
    {
        0 => value,
        1 => value.ToUpperInvariant(),
        _ => char.ToUpperInvariant(value[0]) + value[1..]
    };
}
