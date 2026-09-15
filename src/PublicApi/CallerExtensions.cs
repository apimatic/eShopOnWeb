using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the validated JWT and for keeping personal data out of
/// API responses.
/// </summary>
public static class CallerExtensions
{
    /// <summary>The caller's identity (username) taken from the token, or null if unauthenticated.</summary>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
               ?? user.Identity?.Name
               ?? user.FindFirstValue("unique_name");
    }

    /// <summary>
    /// Masks a phone number for display in responses that an operator (not just the owner) may read,
    /// keeping only the dialling prefix and the last two digits, e.g. "+1********67".
    /// </summary>
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        if (phoneNumber.Length <= 4)
        {
            return new string('*', phoneNumber.Length);
        }

        var prefix = phoneNumber.Substring(0, 2);
        var suffix = phoneNumber.Substring(phoneNumber.Length - 2);
        return prefix + new string('*', phoneNumber.Length - 4) + suffix;
    }
}
