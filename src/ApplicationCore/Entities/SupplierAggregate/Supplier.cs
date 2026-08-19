using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier exposes no API or feed - only a product listing page at <see cref="ProductListingUrl"/>
/// which is read via Firecrawl during a sync.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>
    /// Absolute URL of the supplier's product listing page.
    /// </summary>
    public string ProductListingUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
        if (!Uri.TryCreate(productListingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "productListingUrl must be an absolute http(s) URL.", nameof(productListingUrl));
        }

        ProductListingUrl = productListingUrl;
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618
}
