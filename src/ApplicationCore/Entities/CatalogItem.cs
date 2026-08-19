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
    /// The supplier this item was imported from, if any. Null for items created directly in the store.
    /// </summary>
    public int? SupplierId { get; private set; }

    /// <summary>
    /// The supplier's own stable identifier or URL for this product. Together with <see cref="SupplierId"/>
    /// this is how a re-sync matches a found product back to an existing catalog item instead of duplicating it.
    /// </summary>
    public string? SupplierProductKey { get; private set; }

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
    /// Links this catalog item to the supplier product it was imported from, establishing the
    /// idempotency key used by subsequent syncs.
    /// </summary>
    public void AssignSupplierSource(int supplierId, string supplierProductKey)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Guard.Against.NullOrWhiteSpace(supplierProductKey, nameof(supplierProductKey));
        SupplierId = supplierId;
        SupplierProductKey = supplierProductKey;
    }

    /// <summary>
    /// Applies the latest name/description/price captured from a supplier listing. Unlike
    /// <see cref="UpdateDetails"/> this tolerates a zero (free) price and a missing description,
    /// which real supplier pages sometimes have.
    /// </summary>
    public void UpdateImportedDetails(string name, string? description, decimal price)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.Negative(price, nameof(price));
        Name = name;
        Description = description ?? string.Empty;
        Price = price;
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
