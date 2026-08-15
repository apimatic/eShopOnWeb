using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="BillingUser"/> from the JWT caller identity. The shopper's login (email)
/// is the stable, unique key used as the Maxio customer reference — lower-cased so idempotency
/// holds regardless of how the login was cased at sign-in.
/// </summary>
internal static class BillingUserFactory
{
    public static BillingUser? FromPrincipal(ClaimsPrincipal? principal)
    {
        var email = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name)
            ?? principal?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(email))
            return null;

        email = email.Trim();
        var reference = email.ToLowerInvariant();

        // eShopOnWeb identities carry only an email; derive a display name for the Maxio customer.
        var localPart = email.Split('@').FirstOrDefault();
        var firstName = string.IsNullOrWhiteSpace(localPart) ? email : localPart;
        const string lastName = "eShopOnWeb";

        return new BillingUser(reference, email, firstName, lastName);
    }
}
