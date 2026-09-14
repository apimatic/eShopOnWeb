using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of an eShopOnWeb user for the purposes of billing. The <see cref="Reference"/>
/// is the stable, unique key (the user's login name / email) that maps a single eShop user to a
/// single Maxio customer, which is what makes customer creation idempotent.
/// </summary>
public record EShopSubscriber
{
    /// <summary>Stable, unique reference for the user within eShopOnWeb (login name / email).</summary>
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>
    /// Builds a subscriber from the authenticated user's login name (which in eShopOnWeb is the
    /// user's email). Names are derived from the address purely for display on the Maxio customer
    /// record; the reference/email is what guarantees the one-user-one-customer mapping.
    /// </summary>
    public static EShopSubscriber FromUserName(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? userName : localPart;

        return new EShopSubscriber
        {
            Reference = userName,
            Email = userName,
            FirstName = firstName,
            LastName = "eShopOnWeb"
        };
    }
}
