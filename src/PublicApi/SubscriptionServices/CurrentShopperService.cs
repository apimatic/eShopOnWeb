using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class CurrentShopperService : ICurrentShopperService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentShopperService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<MaxioShopper?> GetCurrentShopperAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return null;
        }

        var email = string.IsNullOrEmpty(user.Email) ? userName : user.Email;
        var firstName = string.Empty;
        var lastName = string.Empty;
        var localPart = email.Split('@')[0].Split('+')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Any(char.IsLetter))
            .ToArray();

        if (tokens.Length == 0)
        {
            firstName = "Customer";
            lastName = userName;
        }
        else if (tokens.Length == 1)
        {
            firstName = Capitalize(tokens[0]);
            lastName = Capitalize(tokens[0]);
        }
        else
        {
            firstName = Capitalize(tokens[0]);
            lastName = string.Join(" ", tokens.Skip(1).Select(Capitalize));
        }

        return new MaxioShopper(user.Id, email, firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
    }
}
