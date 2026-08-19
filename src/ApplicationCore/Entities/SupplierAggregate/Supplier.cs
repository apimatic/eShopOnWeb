using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A third-party supplier whose product listing can be synced into the catalog.
/// The supplier exposes no API or feed - only a product listing page at <see cref="ListingUrl"/>.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>
    /// Absolute URL of the supplier's public product listing page.
    /// </summary>
    public string ListingUrl { get; private set; }

    public Supplier(string name, string listingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));

        Name = name;
        ListingUrl = listingUrl;
    }

    public void UpdateListing(string name, string listingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));

        Name = name;
        ListingUrl = listingUrl;
    }
}
