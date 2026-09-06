using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb shopper a subscription belongs to.
/// </summary>
/// <remarks>
/// Keyed on <see cref="BuyerId"/>, the same value the basket and order aggregates use to identify
/// a buyer, so a shopper is one identity across one-time commerce and recurring billing.
/// </remarks>
public class SubscriberIdentity
{
    public SubscriberIdentity(string buyerId, string email, string? userId = null,
        string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        BuyerId = buyerId;
        Email = email;
        UserId = userId;
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!;
        LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName!;
    }

    /// <summary>The shopper's buyer identity - their user name, matching Basket.BuyerId and Order.BuyerId.</summary>
    public string BuyerId { get; }

    public string Email { get; }

    /// <summary>The ASP.NET Identity user id, carried for logging and support only.</summary>
    public string? UserId { get; }

    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>
    /// The billing customer reference for this shopper. Deterministic, so looking a customer up and
    /// creating one are two halves of the same idempotent operation.
    /// </summary>
    public string CustomerReference => $"eshoponweb-{BuyerId}";

    // eShopOnWeb identities carry no name, and the billing system requires one on every customer.
    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? email : localPart;
    }
}
