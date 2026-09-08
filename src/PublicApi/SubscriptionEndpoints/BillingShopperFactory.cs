using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the <see cref="BillingShopper"/> identity for the current caller from
/// the authenticated JWT (the "name" claim, which is the eShopOnWeb user name).
/// </summary>
public static class BillingShopperFactory
{
    public static BillingShopper? FromPrincipal(ClaimsPrincipal? principal)
    {
        var name = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = principal?.FindFirstValue(ClaimTypes.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new BillingShopper
        {
            Reference = name,
            Email = name
        };
    }
}
