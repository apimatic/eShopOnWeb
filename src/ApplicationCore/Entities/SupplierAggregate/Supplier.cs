using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing page can be synced into the store's own catalog.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's product listing page that a sync will read.</summary>
    public string ProductListingUrl { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public void UpdateListing(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
    }
}
