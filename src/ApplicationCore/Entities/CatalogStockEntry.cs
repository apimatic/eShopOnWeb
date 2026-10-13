using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// The quantity of a catalog item that eShop holds to sell. This is eShop's own stock level —
/// distinct from what Walmart records for the matching SKU. A catalog item with no entry has a
/// level of 0.
/// </summary>
public class CatalogStockEntry : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private CatalogStockEntry() { }

    public CatalogStockEntry(int catalogItemId, int quantity)
    {
        Guard.Against.OutOfRange(catalogItemId, nameof(catalogItemId), 1, int.MaxValue);
        Guard.Against.Negative(quantity, nameof(quantity));

        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }

    public void SetQuantity(int quantity)
    {
        Guard.Against.Negative(quantity, nameof(quantity));
        Quantity = quantity;
    }

    /// <summary>
    /// Takes <paramref name="units"/> off the held level. Throws if more than is held is requested,
    /// so an order can never drive the level negative.
    /// </summary>
    public void Reduce(int units)
    {
        Guard.Against.NegativeOrZero(units, nameof(units));
        if (units > Quantity)
        {
            throw new System.InvalidOperationException(
                $"Cannot reduce stock of catalog item {CatalogItemId} by {units}; only {Quantity} held.");
        }
        Quantity -= units;
    }
}
