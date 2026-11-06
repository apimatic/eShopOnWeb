using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class BillingEndpointHelpers
{
    public const string TAG = "BillingEndpoints";

    /// <summary>The caller's identity, always taken from the validated JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(System.Net.HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return name;
    }
}
