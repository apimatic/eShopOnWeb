using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the billing-system buyer identity from the authenticated eShopOnWeb user.
/// </summary>
public static class BuyerIdentityFactory
{
    public static BuyerIdentity Create(ApplicationUser user)
    {
        var userName = user.Email ?? user.UserName ?? user.Id;
        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;

        string firstName;
        string? lastName;
        var separator = localPart.IndexOfAny(new[] { '.', '_', '-' });
        if (separator > 0)
        {
            firstName = localPart[..separator];
            lastName = localPart[(separator + 1)..];
        }
        else
        {
            firstName = localPart;
            lastName = null;
        }

        return new BuyerIdentity(user.Id, userName, firstName, lastName ?? firstName);
    }
}
