using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

/// <summary>
/// A supplier whose product listing page can be synced into the store catalog.
/// A supplier is identified by an opaque <see cref="Id"/> (surfaced to the API as
/// <c>supplierId</c>) and points at a single public product listing URL.
/// </summary>
public class Supplier : IAggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's public product listing page.</summary>
    public string ProductListingUrl { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));
        Guard.Against.InvalidFormat(productListingUrl, nameof(productListingUrl),
            @"^https?://.+", "productListingUrl must be an absolute http(s) URL");

        Id = Guid.NewGuid();
        Name = name;
        ProductListingUrl = productListingUrl;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618
}
