using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.DigitalFileAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Links catalog items to files in the digital file storage and decides who may download them.
/// </summary>
public class DigitalFileService
{
    private readonly IDigitalFileStorage _storage;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<CatalogItemDigitalFile> _links;
    private readonly IReadRepository<Order> _orders;

    public DigitalFileService(IDigitalFileStorage storage,
        IRepository<CatalogItem> catalogItems,
        IRepository<CatalogItemDigitalFile> links,
        IReadRepository<Order> orders)
    {
        _storage = storage;
        _catalogItems = catalogItems;
        _links = links;
        _orders = orders;
    }

    /// <summary>
    /// Links a catalog item to a file. The file must be one of the files the storage currently offers
    /// (<see cref="IDigitalFileStorage.ListFilesAsync"/>) — an id that merely exists elsewhere is refused.
    /// Re-linking replaces the previous link.
    /// </summary>
    public async Task<LinkDigitalFileResult> LinkAsync(int catalogItemId, string fileId, CancellationToken cancellationToken = default)
    {
        var catalogItem = await _catalogItems.GetByIdAsync(catalogItemId, cancellationToken);
        if (catalogItem is null)
        {
            return LinkDigitalFileResult.CatalogItemNotFound();
        }

        var listing = await _storage.ListFilesAsync(cancellationToken);
        var file = listing.Files.FirstOrDefault(f => string.Equals(f.Id, fileId, StringComparison.Ordinal));
        if (file is null)
        {
            return LinkDigitalFileResult.FileNotOffered(listing.IsTruncated);
        }

        var link = await SaveLinkAsync(catalogItemId, file, cancellationToken);
        return LinkDigitalFileResult.Linked(link);
    }

    private async Task<CatalogItemDigitalFile> SaveLinkAsync(int catalogItemId, DigitalFileInfo file, CancellationToken cancellationToken)
    {
        var existing = await _links.GetByIdAsync(catalogItemId, cancellationToken);
        if (existing is not null)
        {
            existing.Relink(file.Id, file.Name, file.SizeInBytes);
            await _links.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var link = new CatalogItemDigitalFile(catalogItemId, file.Id, file.Name, file.SizeInBytes);
        try
        {
            await _links.AddAsync(link, cancellationToken);
            return link;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The key is the catalog item id, so the store refuses a second insert. If a concurrent request
            // created the link first, PUT semantics apply: the last writer wins, so update it with this file.
            var concurrent = await _links.GetByIdAsync(catalogItemId, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            concurrent.Relink(file.Id, file.Name, file.SizeInBytes);
            await _links.UpdateAsync(concurrent, cancellationToken);
            return concurrent;
        }
    }

    /// <summary>
    /// Decides whether <paramref name="buyerId"/> may download the file of <paramref name="catalogItemId"/>
    /// bought in <paramref name="orderId"/>. Someone else's order is reported as not found so that its
    /// existence is not disclosed.
    /// </summary>
    public async Task<DownloadAuthorization> AuthorizeDownloadAsync(int orderId, int catalogItemId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            return DownloadAuthorization.Denied(DownloadDenialReason.OrderNotFound);
        }

        if (!order.OrderItems.Any(item => item.ItemOrdered.CatalogItemId == catalogItemId))
        {
            return DownloadAuthorization.Denied(DownloadDenialReason.ItemNotInOrder);
        }

        var link = await _links.GetByIdAsync(catalogItemId, cancellationToken);
        if (link is null)
        {
            return DownloadAuthorization.Denied(DownloadDenialReason.NoDigitalFile);
        }

        return DownloadAuthorization.Allowed(link);
    }
}

public enum LinkDigitalFileStatus
{
    Linked,
    CatalogItemNotFound,
    FileNotOffered,
}

public sealed record LinkDigitalFileResult(LinkDigitalFileStatus Status, CatalogItemDigitalFile? Link, bool ListingWasTruncated)
{
    public static LinkDigitalFileResult Linked(CatalogItemDigitalFile link) => new(LinkDigitalFileStatus.Linked, link, false);
    public static LinkDigitalFileResult CatalogItemNotFound() => new(LinkDigitalFileStatus.CatalogItemNotFound, null, false);
    public static LinkDigitalFileResult FileNotOffered(bool listingWasTruncated) => new(LinkDigitalFileStatus.FileNotOffered, null, listingWasTruncated);
}

public enum DownloadDenialReason
{
    None,
    OrderNotFound,
    ItemNotInOrder,
    NoDigitalFile,
}

public sealed record DownloadAuthorization(DownloadDenialReason DenialReason, CatalogItemDigitalFile? File)
{
    public bool IsAllowed => DenialReason == DownloadDenialReason.None && File is not null;

    public static DownloadAuthorization Allowed(CatalogItemDigitalFile file) => new(DownloadDenialReason.None, file);
    public static DownloadAuthorization Denied(DownloadDenialReason reason) => new(reason, null);
}
