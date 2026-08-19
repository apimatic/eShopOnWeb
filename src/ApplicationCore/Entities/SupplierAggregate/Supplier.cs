using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A third-party supplier whose product listing can be synced into the store's own catalog.
/// A supplier is identified by a name and the URL of the product listing page that is read
/// during a sync.
/// </summary>
public class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }
    public string ListingUrl { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    public Supplier(string name, string listingUrl)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        ListingUrl = Guard.Against.InvalidHttpUrl(listingUrl, nameof(listingUrl));
        RegisteredAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private Supplier() { }
#pragma warning restore CS8618
}
