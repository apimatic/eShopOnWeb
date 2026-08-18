using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's stable identity, used as the buyer id for their orders, numbers and notifications.
    /// Read from the JWT's name claim (issued as <see cref="ClaimTypes.Name"/> by the token service),
    /// with a fallback to the raw <c>unique_name</c> claim in case inbound claim-type mapping is off.
    /// </summary>
    public static string? GetCallerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name");
    }
}
