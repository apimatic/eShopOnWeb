using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being enrolled into a subscription, together with the stable
/// key that identifies it in the billing provider.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the idempotency anchor for the whole integration: the billing
/// provider enforces uniqueness on a customer reference, so deriving it deterministically from
/// the eShopOnWeb user name means the same shopper always resolves to the same billing customer -
/// including across process restarts.
/// </para>
/// <para>
/// The user *name* is used rather than the Identity primary key on purpose. eShopOnWeb already
/// keys its own domain off the user name (Basket.BuyerId, Order.BuyerId), and the Identity key is
/// regenerated whenever the store is re-seeded - which would orphan the billing customer created
/// on the previous run.
/// </para>
/// </remarks>
public class Subscriber
{
    /// <summary>Namespace prefix, so an eShopOnWeb reference never collides with one from another app on the same billing site.</summary>
    public const string ReferencePrefix = "eshoponweb-";

    private const string DefaultName = "eShopOnWeb";

    public Subscriber(string userName, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        UserName = userName.Trim();
        Email = email.Trim();
        Reference = ReferencePrefix + UserName.ToLowerInvariant();

        FirstName = FirstNonBlank(firstName, Email.Split('@')[0], DefaultName);
        LastName = FirstNonBlank(lastName, DefaultName, DefaultName);
    }

    /// <summary>The eShopOnWeb user name taken from the caller's token.</summary>
    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>The stable, provider-side customer reference for this shopper.</summary>
    public string Reference { get; }

    /// <summary>The reference recorded on the subscription itself, so one can be traced back to a shopper and plan.</summary>
    public string SubscriptionReference(string planHandle)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        return Reference + "-" + planHandle.Trim().ToLowerInvariant();
    }

    private static string FirstNonBlank(string? preferred, string? fallback, string lastResort)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred!.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? lastResort : fallback!.Trim();
    }
}
