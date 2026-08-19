using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A supplier whose product listing can be synced into the store's own catalog.
/// A supplier is identified by a display <see cref="Name"/> and the <see cref="ListingUrl"/>
/// of the page where its products are published.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }

    /// <summary>The URL of the supplier's product listing page that gets scraped during a sync.</summary>
    public string ListingUrl { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public Supplier(string name, string listingUrl)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(listingUrl, nameof(listingUrl));
        Guard.Against.InvalidListingUrl(listingUrl, nameof(listingUrl));

        Name = name;
        ListingUrl = listingUrl;
        RegisteredAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618
}
