using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The authenticated shopper's identity, derived from the JWT. In eShopOnWeb the user name is the email,
/// which serves as the stable, unique reference used to key the Maxio customer.
/// </summary>
public record ShopperIdentity(string Reference, string Email, string FirstName, string LastName)
{
    /// <summary>Extracts the shopper identity from the JWT claims, or <c>null</c> when unauthenticated.</summary>
    public static ShopperIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name)
            ?? principal?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = principal?.FindFirstValue(ClaimTypes.Email) ?? userName;

        // eShopOnWeb identities carry no separate given/family name, so derive a friendly first name from the
        // email local-part. Maxio requires both a first and last name when creating a customer.
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new ShopperIdentity(userName, email, firstName, "eShopOnWeb");
    }
}
