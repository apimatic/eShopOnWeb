using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Maps an eShopOnWeb user onto the billing system's customer <c>reference</c>.
/// <para>
/// The mapping is a pure function of the user name, so it needs no local storage: the same shopper
/// resolves to the same billing customer after an application restart, after the identity database
/// is recreated, and from any instance of the application. The billing provider enforces
/// uniqueness on this value, which is what makes "ensure a customer exists" idempotent.
/// </para>
/// </summary>
public static class BillingReferences
{
    /// <summary>Namespace prefix so several applications can safely share one billing site.</summary>
    public const string Prefix = "eshoponweb-";

    public static string ForUser(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        return Prefix + userName.Trim().ToLowerInvariant();
    }
}
