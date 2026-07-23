using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb-side view of a customer's subscription: the link between an eShopOnWeb user
/// and the billing provider's customer/subscription records, plus the plan and state last read
/// from the provider.
/// </summary>
/// <remarks>
/// The provider is the system of record (§1.1) and the userId-to-subscription mapping is kept
/// stateless, idempotent on <see cref="BuyerId"/> (§8). This aggregate is therefore built from a
/// provider read on each request rather than loaded from <c>CatalogContext</c>. It is shaped as a
/// <see cref="BaseEntity"/> aggregate root so that turning on persistence later is an EF mapping
/// plus a migration, not a redesign; until then <see cref="BaseEntity.Id"/> stays 0.
/// </remarks>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(string buyerId, BillingSubscription billing)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Billing = Guard.Against.Null(billing, nameof(billing));
    }

    /// <summary>
    /// The eShopOnWeb user this subscription belongs to — the username/email from
    /// <c>User.Identity.Name</c>, which is also the provider-side customer reference (§4.4).
    /// </summary>
    public string BuyerId { get; private set; }

    /// <summary>The provider's current view of this subscription.</summary>
    public BillingSubscription Billing { get; private set; }

    public long ProviderSubscriptionId => Billing.Id;

    public long ProviderCustomerId => Billing.CustomerId;

    public string PlanHandle => Billing.ProductHandle;

    public SubscriptionState State => Billing.State;

    public bool IsActive => Billing.IsActive;

    /// <summary>Replaces the cached provider view after a lifecycle or plan change.</summary>
    public void RefreshFrom(BillingSubscription billing)
    {
        Billing = Guard.Against.Null(billing, nameof(billing));
    }
}
