using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier exposes only a product listing page (no API/feed), so the store reads
/// that page and imports the products it finds.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>
    /// The URL of the supplier's product listing page that a sync reads.
    /// </summary>
    public string ProductListingUrl { get; private set; }

    public System.DateTimeOffset CreatedAt { get; private set; }

    private Supplier()
    {
        // Required by EF Core.
        Name = string.Empty;
        ProductListingUrl = string.Empty;
    }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
        CreatedAt = System.DateTimeOffset.UtcNow;
    }

    public void UpdateListingUrl(string productListingUrl)
    {
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
    }
}
