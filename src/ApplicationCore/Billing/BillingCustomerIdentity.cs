using System;
using System.Globalization;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The shopper as the billing provider needs to see them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the linchpin of the whole integration: it is derived deterministically
/// from the authenticated user name, so the same shopper always resolves to the same provider
/// customer. That is what makes "ensure a customer exists" idempotent without eShopOnWeb storing a
/// user-id-to-customer-id mapping of its own — which matters here, because the sample can run on an
/// in-memory database that is wiped on every restart.
/// </para>
/// <para>
/// eShopOnWeb's identity records carry no given/family name, so both are synthesised from the email
/// local part. The provider requires them; nothing downstream depends on their exact value.
/// </para>
/// </remarks>
public class BillingCustomerIdentity
{
    /// <summary>Prefix on every reference, so provider-side records are obviously ours.</summary>
    public const string ReferencePrefix = "eshop-";

    private const string FallbackLastName = "Customer";

    public BillingCustomerIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable external id written to the provider customer record.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds the provider identity for an authenticated eShopOnWeb user. The user name is the
    /// email address in this application (identity seeding sets <c>UserName == Email</c>), and it is
    /// the only identity the PublicApi JWT carries.
    /// </summary>
    public static BillingCustomerIdentity ForUserName(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var normalized = userName.Trim().ToLowerInvariant();
        var localPart = normalized.Split('@')[0];
        var words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 0)
            .ToArray();

        var firstName = words.Length > 0 ? Capitalize(words[0]) : normalized;
        var lastName = words.Length > 1
            ? string.Join(" ", words.Skip(1).Select(Capitalize))
            : FallbackLastName;

        return new BillingCustomerIdentity(ReferencePrefix + normalized, normalized, firstName, lastName);
    }

    /// <summary>
    /// Deterministic reference for one shopper's subscription to one plan. It lets a subscription
    /// created by a request whose response never arrived be recognised afterwards.
    /// </summary>
    public string SubscriptionReferenceFor(string planHandle)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        return $"{Reference}-{planHandle.Trim().ToLowerInvariant()}";
    }

    private static string Capitalize(string word) =>
        word.Length == 1
            ? word.ToUpperInvariant()
            : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word.Substring(1);
}
