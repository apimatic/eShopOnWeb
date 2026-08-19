using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing the store can sync into its own catalog. A supplier offers
/// no API or feed, only a product listing page reachable at <see cref="ProductListingUrl"/>.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }
    public string ProductListingUrl { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = Guard.Against.InvalidFormat(
            Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl)),
            nameof(productListingUrl),
            @"^https?://.+");
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public void UpdateListingUrl(string productListingUrl)
    {
        ProductListingUrl = Guard.Against.InvalidFormat(
            Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl)),
            nameof(productListingUrl),
            @"^https?://.+");
    }
}
