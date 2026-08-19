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

    /// <summary>
    /// The URL of the supplier's public product listing page that a sync reads from.
    /// </summary>
    public string ProductListingUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Supplier(string name, string productListingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));

        Name = name;
        ProductListingUrl = productListingUrl;
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618
}
