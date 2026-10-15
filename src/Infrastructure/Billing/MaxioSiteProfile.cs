using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Site-wide Maxio settings the subscribe flow depends on.
/// </summary>
/// <param name="InvoiceCollectionMethod">
/// The collection method that bills by invoice instead of charging a payment method on file — <c>remittance</c> on
/// Relationship Invoicing sites, <c>invoice</c> on legacy Statements sites. This store captures no payment method,
/// so subscriptions are created with it.
/// </param>
/// <param name="Currency">The site's currency, used to label plan prices.</param>
public record MaxioSiteProfile(CollectionMethod InvoiceCollectionMethod, string? Currency);

/// <summary>Process-wide cache of the <see cref="MaxioSiteProfile"/>; the settings it holds change only by site administration.</summary>
public class MaxioSiteProfileCache
{
    private volatile MaxioSiteProfile? _profile;

    public MaxioSiteProfile? Current => _profile;

    public void Set(MaxioSiteProfile profile) => _profile = profile;
}
