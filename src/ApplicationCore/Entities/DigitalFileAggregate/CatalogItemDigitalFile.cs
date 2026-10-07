using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.DigitalFileAggregate;

/// <summary>
/// Links a catalog item to the downloadable file that is delivered to shoppers who buy it.
/// The key is the catalog item id, so a catalog item has at most one linked file.
/// </summary>
public class CatalogItemDigitalFile : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private CatalogItemDigitalFile() { }

    public CatalogItemDigitalFile(int catalogItemId, string fileId, string fileName, long? sizeInBytes)
    {
        Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        Id = catalogItemId;
        Relink(fileId, fileName, sizeInBytes);
    }

    public int CatalogItemId => Id;

    /// <summary>The file's id in the merchant's file storage (Box).</summary>
    public string FileId { get; private set; }

    /// <summary>The file name at the time it was linked.</summary>
    public string FileName { get; private set; }

    public long? SizeInBytes { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public void Relink(string fileId, string fileName, long? sizeInBytes)
    {
        Guard.Against.NullOrWhiteSpace(fileId, nameof(fileId));
        Guard.Against.NullOrWhiteSpace(fileName, nameof(fileName));

        FileId = fileId;
        FileName = fileName;
        SizeInBytes = sizeInBytes;
        LinkedAt = DateTimeOffset.UtcNow;
    }
}
