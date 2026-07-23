using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing action is performed.
/// </summary>
/// <remarks>
/// <see cref="Reference"/> is the stable reference that makes provider-side customer creation
/// idempotent across repeated subscribe calls. Per the integration's decisions this is the
/// signed-in user's email / username.
/// </remarks>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string? email = null, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));

        Reference = reference;
        Email = string.IsNullOrWhiteSpace(email) ? reference : email;
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(Email) : firstName;
        LastName = string.IsNullOrWhiteSpace(lastName) ? DefaultLastName : lastName;
    }

    /// <summary>Used when the identity carries no surname; the provider requires a non-empty last name.</summary>
    private const string DefaultLastName = "eShopOnWeb";

    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Derives a display-worthy first name from an email address, so the provider record is never
    /// created with an empty required name field.
    /// </summary>
    private static string DeriveFirstName(string email)
    {
        var atIndex = email.IndexOf('@');
        var local = atIndex > 0 ? email[..atIndex] : email;
        return string.IsNullOrWhiteSpace(local) ? "Customer" : local;
    }
}
