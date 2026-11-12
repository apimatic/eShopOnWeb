using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The caller's identity, always taken from the validated JWT.</summary>
public static class Caller
{
    public static string Name(ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(401, "The token does not identify a user.");
        return name;
    }
}
