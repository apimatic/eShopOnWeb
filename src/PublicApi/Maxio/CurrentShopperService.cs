using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Resolves the authenticated caller into a <see cref="ShopperIdentity"/>. The stable reference is the
/// JWT user name (the shopper's email/login), which is deterministic across app restarts — so the same
/// Maxio customer is reused run to run, unlike the regenerated in-memory identity keys.
/// </summary>
public class CurrentShopperService : ICurrentShopperService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentShopperService(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<ShopperIdentity> GetCurrentShopperAsync(CancellationToken ct)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioBillingException("No authenticated user.", HttpStatusCode.Unauthorized);
        }

        // The user name is the stable per-shopper reference. Look up the identity record to source a
        // real email for the Maxio customer; fall back to the user name if the record is unavailable.
        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? userName;

        var (firstName, lastName) = SplitName(email);
        return new ShopperIdentity(Reference: userName.Trim(), Email: email, FirstName: firstName, LastName: lastName);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return (firstName, "Shopper");
    }
}
