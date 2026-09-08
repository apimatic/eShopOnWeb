using System.Globalization;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class MaxioUserInfoFactory
{
    public static MaxioUserInfo FromUser(ApplicationUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email! : user.UserName ?? $"{user.Id}@example.invalid";
        var (firstName, lastName) = DeriveNames(email);

        return new MaxioUserInfo
        {
            Reference = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var parts = email.Split('@', 2);
        return (Capitalize(parts[0]), Capitalize(parts.Length > 1 ? parts[1] : "Shopper"));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
    }
}
