using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class BuyerIdentity
{
    public static string Require(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApplicationCore.Exceptions.ForbiddenOperationException("A signed-in shopper is required.");
        }

        return name;
    }
}
