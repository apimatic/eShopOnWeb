using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Who is performing a subscription action. Customers may only act on their own subscription;
/// administrators may act on any (plan.md UC2/UC4, actor).
/// </summary>
public sealed record SubscriptionActor
{
    private SubscriptionActor(string userName, bool isAdministrator)
    {
        UserName = userName;
        IsAdministrator = isAdministrator;
    }

    /// <summary>The acting eShopOnWeb user's email/username.</summary>
    public string UserName { get; }

    public bool IsAdministrator { get; }

    public static SubscriptionActor Customer(string userName) =>
        new(Require(userName), isAdministrator: false);

    public static SubscriptionActor Administrator(string userName) =>
        new(Require(userName), isAdministrator: true);

    /// <summary>
    /// True when this actor is allowed to act on a subscription owned by <paramref name="customerReference"/>.
    /// </summary>
    public bool CanAct(string? customerReference) =>
        IsAdministrator ||
        (!string.IsNullOrEmpty(customerReference) &&
         string.Equals(customerReference, UserName, StringComparison.OrdinalIgnoreCase));

    private static string Require(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("An acting user name is required.", nameof(userName));
        }

        return userName.Trim();
    }
}
