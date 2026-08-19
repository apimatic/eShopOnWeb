using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing page can be synced into the store catalog.
/// Suppliers are identified externally by a stable <see cref="Guid"/> so the API never
/// leaks sequential database keys.
/// </summary>
public class Supplier : IAggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's product listing page.</summary>
    public string ListingUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Supplier(string name, string listingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));

        Id = Guid.NewGuid();
        Name = name.Trim();
        ListingUrl = listingUrl.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618

    public void UpdateListing(string name, string listingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));
        Name = name.Trim();
        ListingUrl = listingUrl.Trim();
    }
}
