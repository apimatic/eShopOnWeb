using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A third-party supplier whose product listing page can be synced into the store catalog.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>
    /// The URL of the supplier's public product listing page (the entry point for a sync).
    /// </summary>
    public string ListingUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618

    public Supplier(string name, string listingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ListingUrl = Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
