using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper being billed. The identity is derived from the
/// authenticated caller (JWT), never from client-supplied input, so a shopper can only
/// ever act on their own billing account. The <see cref="Reference"/> is the stable key
/// used to look up / create the matching Maxio customer, which makes the "ensure a
/// customer exists" step idempotent.
/// </summary>
public class SubscriberIdentity
{
    private const string ReferencePrefix = "eshoponweb:";

    public SubscriberIdentity(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        UserName = userName;
        Email = userName; // eShopOnWeb identity uses the email address as the user name.
        Reference = ReferencePrefix + userName.Trim().ToLowerInvariant();
    }

    /// <summary>The authenticated user name (an email address in eShopOnWeb).</summary>
    public string UserName { get; }

    /// <summary>The shopper's email address.</summary>
    public string Email { get; }

    /// <summary>
    /// Stable external identifier stored on the Maxio customer's <c>reference</c> field.
    /// Maxio enforces uniqueness on this value, guaranteeing one customer per shopper.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// A best-effort first name derived from the email local part, used only when a new
    /// Maxio customer has to be created (Maxio requires first/last name).
    /// </summary>
    public string FirstName
    {
        get
        {
            var at = Email.IndexOf('@');
            var local = at > 0 ? Email.Substring(0, at) : Email;
            return string.IsNullOrWhiteSpace(local) ? Email : local;
        }
    }

    /// <summary>A stable placeholder last name marking the customer as eShopOnWeb-originated.</summary>
    public string LastName => "eShopOnWeb";
}
