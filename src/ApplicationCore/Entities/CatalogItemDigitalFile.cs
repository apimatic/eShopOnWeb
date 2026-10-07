using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Links a catalog item to the digital file a buyer receives for it. The key is the catalog item id,
/// so an item has at most one linked file and relinking replaces it.
/// </summary>
public class CatalogItemDigitalFile : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private CatalogItemDigitalFile() { }

    public CatalogItemDigitalFile(int catalogItemId, string fileId, string fileName, long? sizeBytes)
    {
        Guard.Against.OutOfRange(catalogItemId, nameof(catalogItemId), 1, int.MaxValue);
        Id = catalogItemId;
        Relink(fileId, fileName, sizeBytes);
    }

    public int CatalogItemId => Id;

    /// <summary>The id of the file at the storage provider.</summary>
    public string FileId { get; private set; }

    /// <summary>The file name as the provider reported it when the link was made.</summary>
    public string FileName { get; private set; }

    public long? SizeBytes { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public void Relink(string fileId, string fileName, long? sizeBytes)
    {
        Guard.Against.NullOrWhiteSpace(fileId, nameof(fileId));
        Guard.Against.NullOrWhiteSpace(fileName, nameof(fileName));

        FileId = fileId;
        FileName = fileName;
        SizeBytes = sizeBytes;
        LinkedAt = DateTimeOffset.UtcNow;
    }
}
