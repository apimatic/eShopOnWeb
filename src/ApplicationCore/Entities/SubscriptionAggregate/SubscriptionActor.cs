using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Who is performing a subscription operation. A customer may only act on their own
/// subscriptions; an administrator may act on any. Making this an explicit, typed argument means
/// a caller cannot reach a cross-customer operation by forgetting an ownership check.
/// </summary>
public sealed class SubscriptionActor
{
    private SubscriptionActor(string? userName, bool isAdministrator)
    {
        UserName = userName;
        IsAdministrator = isAdministrator;
    }

    /// <summary>
    /// The eShopOnWeb user reference (email / username) the actor owns subscriptions under, or
    /// <see langword="null"/> for an administrator acting across users.
    /// </summary>
    public string? UserName { get; }

    /// <summary>Whether ownership checks are bypassed for this actor.</summary>
    public bool IsAdministrator { get; }

    /// <summary>
    /// An authenticated shopper acting on their own subscriptions. Ownership is enforced against
    /// <paramref name="userName"/> before any state-changing provider call.
    /// </summary>
    public static SubscriptionActor Customer(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A customer actor requires a user name.", nameof(userName));
        }

        return new SubscriptionActor(userName, isAdministrator: false);
    }

    /// <summary>An administrator acting on any customer's subscription.</summary>
    public static SubscriptionActor Administrator() => new(userName: null, isAdministrator: true);
}
