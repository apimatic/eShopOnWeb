using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier exposes only a product listing <em>page</em> (no API/feed), so the store
/// reads that page to bring the supplier's products into the catalog.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's product listing page.</summary>
    public string ProductListingUrl { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.InvalidHttpUrl(productListingUrl, nameof(productListingUrl));
    }

    public void UpdateListing(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.InvalidHttpUrl(productListingUrl, nameof(productListingUrl));
    }
}
