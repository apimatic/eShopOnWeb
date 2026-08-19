using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A product supplier whose catalog is imported into the store by scraping its
/// public product-listing page. This is the aggregate root for the supplier-sync feature.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>
    /// The URL of the supplier's public product-listing page that syncs read from.
    /// </summary>
    public string ProductListingUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateListing(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
    }
}
