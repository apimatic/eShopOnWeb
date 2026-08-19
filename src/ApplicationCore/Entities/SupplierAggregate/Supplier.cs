using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier offers no API or feed &mdash; only a public product listing page,
/// identified by <see cref="ProductListingUrl"/>.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618

    public Supplier(string name, string productListingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));

        var trimmedUrl = productListingUrl.Trim();
        if (!IsAbsoluteHttpUrl(trimmedUrl))
        {
            throw new ArgumentException(
                "Product listing URL must be an absolute http(s) URL.", nameof(productListingUrl));
        }

        Name = name.Trim();
        ProductListingUrl = trimmedUrl;
    }

    /// <summary>Human-friendly supplier name.</summary>
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's product listing page.</summary>
    public string ProductListingUrl { get; private set; }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
