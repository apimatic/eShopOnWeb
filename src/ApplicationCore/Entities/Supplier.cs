using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier exposes only a product listing page (no API/feed), identified by <see cref="ProductListingUrl"/>.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }
    public string ProductListingUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = GuardAgainstInvalidUrl(productListingUrl);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateListing(string name, string productListingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ProductListingUrl = GuardAgainstInvalidUrl(productListingUrl);
    }

    private static string GuardAgainstInvalidUrl(string url)
    {
        Guard.Against.NullOrWhiteSpace(url, nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{url}' is not a valid absolute http(s) URL.", nameof(url));
        }
        return url;
    }
}
