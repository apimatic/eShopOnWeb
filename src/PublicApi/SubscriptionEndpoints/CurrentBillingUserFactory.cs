using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CurrentBillingUserFactory
{
    private const string LocalIssuer = "eshoponweb-identity";
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentBillingUserFactory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<CurrentBillingUser> CreateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated is not true ||
            string.IsNullOrWhiteSpace(principal.Identity.Name))
        {
            throw SubscriptionBillingException.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(principal.Identity.Name);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw SubscriptionBillingException.Unauthorized();
        }

        var issuer = principal.FindFirst("iss")?.Value ?? LocalIssuer;
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value
            ?? user.Id;
        var userKey = Hash($"{issuer}\n{subject}");
        var firstName = principal.FindFirst(ClaimTypes.GivenName)?.Value;
        var lastName = principal.FindFirst(ClaimTypes.Surname)?.Value;

        return new CurrentBillingUser(
            userKey,
            user.Email,
            string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
            string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName,
            $"eshop-user:{userKey}");
    }

    public static string SubscriptionReference(string customerReference, string productHandle) =>
        $"eshop-subscription:{Hash($"{customerReference}\n{productHandle.ToLowerInvariant()}")}";

    private static string Hash(string value) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
