using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class CatalogItem : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public decimal Price { get; private set; }
    public string PictureUri { get; private set; }
    public int CatalogTypeId { get; private set; }
    public CatalogType? CatalogType { get; private set; }
    public int CatalogBrandId { get; private set; }
    public CatalogBrand? CatalogBrand { get; private set; }

    /// <summary>
    /// How many units of this item the shop currently holds and can sell.
    /// An item whose level has never been set holds 0. This is eShop's own level;
    /// it is pushed to Walmart only at the next sync, never implicitly.
    /// </summary>
    public int AvailableStock { get; private set; }

    public CatalogItem(int catalogTypeId,
        int catalogBrandId,
        string description,
        string name,
        decimal price,
        string pictureUri)
    {
        CatalogTypeId = catalogTypeId;
        CatalogBrandId = catalogBrandId;
        Description = description;
        Name = name;
        Price = price;
        PictureUri = pictureUri;
    }

    public void UpdateDetails(CatalogItemDetails details)
    {
        Guard.Against.NullOrEmpty(details.Name, nameof(details.Name));
        Guard.Against.NullOrEmpty(details.Description, nameof(details.Description));
        Guard.Against.NegativeOrZero(details.Price, nameof(details.Price));

        Name = details.Name;
        Description = details.Description;
        Price = details.Price;
    }

    public void UpdateBrand(int catalogBrandId)
    {
        Guard.Against.Zero(catalogBrandId, nameof(catalogBrandId));
        CatalogBrandId = catalogBrandId;
    }

    public void UpdateType(int catalogTypeId)
    {
        Guard.Against.Zero(catalogTypeId, nameof(catalogTypeId));
        CatalogTypeId = catalogTypeId;
    }

    /// <summary>
    /// Sets eShop's own stock level for this item to an absolute quantity. Does not touch Walmart.
    /// </summary>
    public void SetStockLevel(int quantity)
    {
        Guard.Against.Negative(quantity, nameof(quantity));
        AvailableStock = quantity;
    }

    /// <summary>
    /// Removes the given quantity from eShop's own stock level (e.g. when an order is placed).
    /// Throws <see cref="InsufficientStockException"/> if the shop does not hold enough.
    /// </summary>
    public void RemoveStock(int quantity)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));
        if (quantity > AvailableStock)
        {
            throw new InsufficientStockException(
                $"Cannot remove {quantity} unit(s): only {AvailableStock} in stock for catalog item {Id}.");
        }
        AvailableStock -= quantity;
    }

    public void UpdatePictureUri(string pictureName)
    {
        if (string.IsNullOrEmpty(pictureName))
        {
            PictureUri = string.Empty;
            return;
        }
        PictureUri = $"images\\products\\{pictureName}?{new DateTime().Ticks}";
    }

    public readonly record struct CatalogItemDetails
    {
        public string? Name { get; }
        public string? Description { get; }
        public decimal Price { get; }

        public CatalogItemDetails(string? name, string? description, decimal price)
        {
            Name = name;
            Description = description;
            Price = price;
        }
    }
}
