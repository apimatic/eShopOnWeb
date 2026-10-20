using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>What the billing customer is created with. Never logged.</summary>
public sealed record ShopperProfile(string Email, string FirstName, string LastName)
{
    public const string FallbackFirstName = "eShop";
    public const string FallbackLastName = "Customer";

    /// <summary>
    /// Derives first/last name from the e-mail local part (the Identity user has no name fields):
    /// "jane.doe@x" gives "Jane" "Doe"; a single token gives that token and "Customer"; nothing usable gives "eShop" "Customer".
    /// </summary>
    public static ShopperProfile FromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetter).ToArray()))
            .Where(t => t.Length > 0)
            .ToArray();

        var first = tokens.Length > 0 ? Capitalize(tokens[0]) : FallbackFirstName;
        var last = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1).Select(Capitalize)) : FallbackLastName;
        return new ShopperProfile(email, first, last);
    }

    private static string Capitalize(string value) =>
        char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..].ToLowerInvariant();
}

/// <summary>Resolves the profile of the shop user named in the caller's token.</summary>
public interface IShopperProfileProvider
{
    /// <summary>The profile, or <c>null</c> when the user has no usable e-mail address.</summary>
    Task<ShopperProfile?> GetAsync(string userName);
}

public sealed class IdentityShopperProfileProvider : IShopperProfileProvider
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityShopperProfileProvider(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ShopperProfile?> GetAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email)
            ? user!.Email!
            : (userName.Contains('@') ? userName : null);
        return email == null ? null : ShopperProfile.FromEmail(email.Trim());
    }
}
