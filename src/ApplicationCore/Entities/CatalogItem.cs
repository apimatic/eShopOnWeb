using System;
using Ardalis.GuardClauses;
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
    /// The number of units eShop currently holds in stock for this item. An item whose stock has
    /// never been set holds 0. This is eShop's own level and is independent of what any external
    /// sales channel (e.g. Walmart Marketplace) records for it.
    /// </summary>
    public int StockQuantity { get; private set; }

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
    /// Sets eShop's stock level for this item to an absolute quantity. Does not touch any
    /// external sales channel; a marketplace learns the new level at the next sync.
    /// </summary>
    public void SetStock(int quantity)
    {
        Guard.Against.Negative(quantity, nameof(quantity));
        StockQuantity = quantity;
    }

    /// <summary>
    /// Takes <paramref name="quantity"/> units off eShop's stock for this item (e.g. when an
    /// order is placed). Throws <see cref="Exceptions.InsufficientStockException"/> when the shop
    /// holds fewer units than requested.
    /// </summary>
    public void RemoveStock(int quantity)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));
        if (quantity > StockQuantity)
        {
            throw new Exceptions.InsufficientStockException(Id, StockQuantity, quantity);
        }
        StockQuantity -= quantity;
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
