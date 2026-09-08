using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The authenticated caller, mapped to the Maxio customer that represents them.
/// </summary>
/// <remarks>
/// The customer is keyed by a deterministic <see cref="Reference"/> derived from the caller's
/// stable login name (the e-mail, which is also the user name in this app). Maxio enforces a
/// single customer per reference, which is what makes find-or-create idempotent: a double-click
/// can never produce two customers for one user, and a user who returns later resolves to the
/// same customer and therefore the same subscriptions. eShopOnWeb stores no personal names, so
/// the Maxio first/last name is derived from the e-mail local/domain parts.
/// </remarks>
public sealed class MaxioCustomer
{
    public MaxioCustomer(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An e-mail address is required.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant();
        Reference = "eshop-" + normalized;

        var parts = normalized.Split('@');
        var local = parts[0].Trim();
        var domain = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var lastName = domain.Split('.')[0];
        FirstName = string.IsNullOrEmpty(local) ? "eShop" : local;
        LastName = string.IsNullOrEmpty(lastName) ? "User" : lastName;
        Email = normalized;
    }

    /// <summary>Deterministic Maxio customer reference (unique per user).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Maps an authenticated <see cref="ClaimsPrincipal"/> to a <see cref="MaxioCustomer"/> using
    /// the token's name claim (the login e-mail), or null when the principal carries no name.
    /// </summary>
    public static MaxioCustomer? FromPrincipal(ClaimsPrincipal? principal)
    {
        var email = principal?.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.Name)
            : null;
        return string.IsNullOrWhiteSpace(email) ? null : new MaxioCustomer(email);
    }
}
