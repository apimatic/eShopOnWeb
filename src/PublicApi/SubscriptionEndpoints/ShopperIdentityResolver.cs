using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ShopperIdentityNotFoundException : Exception
{
    public ShopperIdentityNotFoundException()
        : base("The authenticated shopper identity is incomplete.") { }
}

public interface IShopperIdentityResolver
{
    Task<ShopperIdentity> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class ShopperIdentityResolver : IShopperIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ShopperIdentityResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ShopperIdentity> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new ShopperIdentityNotFoundException();
        }

        var claimSubject = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                           principal.FindFirstValue("sub");
        var userName = principal.Identity.Name;
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(claimSubject))
        {
            user = await _userManager.FindByIdAsync(claimSubject);
        }

        if (user is null && !string.IsNullOrWhiteSpace(userName))
        {
            user = await _userManager.FindByNameAsync(userName);
        }

        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            throw new ShopperIdentityNotFoundException();
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ShopperIdentityNotFoundException();
        }

        var fallbackName = email.Split('@', 2)[0]
            .Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Shopper";
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName) ?? fallbackName;
        var lastName = principal.FindFirstValue(ClaimTypes.Surname) ?? "eShopOnWeb";

        // eShopOnWeb uses the normalized user name as its durable buyer identity. It is
        // also stable when the development in-memory Identity store is reseeded.
        var stableSubject = user.NormalizedUserName ?? user.UserName ?? user.Id;
        return new ShopperIdentity(stableSubject, email, firstName, lastName);
    }
}
