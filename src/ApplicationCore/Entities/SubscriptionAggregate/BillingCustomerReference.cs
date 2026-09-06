using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Builds the stable key that maps an eShopOnWeb user onto a billing customer.
/// </summary>
/// <remarks>
/// The mapping deliberately lives in the billing system (as the customer unique
/// <c>reference</c>) rather than in the eShopOnWeb database: the reference survives an
/// application restart even when eShopOnWeb runs on the in-memory provider, and it keeps
/// the billing system the single source of truth for who is subscribed to what.
/// The user name is used - rather than the ASP.NET Identity id - because identity ids are
/// regenerated on every restart when the in-memory provider is in use.
/// </remarks>
public static class BillingCustomerReference
{
    public const string Prefix = "eshop:";

    public static string ForUser(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        return Prefix + userName.Trim().ToLowerInvariant();
    }
}
