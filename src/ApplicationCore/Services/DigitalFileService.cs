using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public enum DigitalFileLinkStatus
{
    Linked,
    CatalogItemNotFound,
    /// <summary>The file is not one the provider offers in the product folder.</summary>
    FileNotOffered,
    /// <summary>The file is in the product folder but its details could not be read.</summary>
    FileUnreadable,
    /// <summary>The file was not in the part of the product folder that could be listed.</summary>
    ListingIncomplete,
}

public sealed record DigitalFileLinkResult(DigitalFileLinkStatus Status, CatalogItemDigitalFile? Link = null);

public enum DigitalDownloadStatus
{
    Authorized,
    /// <summary>No such order, or the order belongs to someone else (deliberately indistinguishable).</summary>
    OrderNotFound,
    ItemNotInOrder,
    NoDigitalFile,
}

public sealed record DigitalDownloadAuthorization(DigitalDownloadStatus Status, CatalogItemDigitalFile? Link = null);

/// <summary>
/// Links catalog items to digital files and decides who may download them.
/// </summary>
public class DigitalFileService
{
    private readonly IDigitalFileProvider _provider;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<CatalogItemDigitalFile> _linkRepository;
    private readonly IReadRepository<Order> _orderRepository;

    public DigitalFileService(IDigitalFileProvider provider,
        IRepository<CatalogItem> itemRepository,
        IRepository<CatalogItemDigitalFile> linkRepository,
        IReadRepository<Order> orderRepository)
    {
        _provider = provider;
        _itemRepository = itemRepository;
        _linkRepository = linkRepository;
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Links <paramref name="catalogItemId"/> to <paramref name="fileId"/>. The file must be one the provider
    /// lists in the product folder; anything else is refused before anything is saved.
    /// </summary>
    public async Task<DigitalFileLinkResult> LinkAsync(int catalogItemId, string fileId, CancellationToken cancellationToken = default)
    {
        var item = await _itemRepository.GetByIdAsync(catalogItemId, cancellationToken);
        if (item is null)
            return new DigitalFileLinkResult(DigitalFileLinkStatus.CatalogItemNotFound);

        var listing = await _provider.ListFilesAsync(cancellationToken);
        var file = listing.Files.FirstOrDefault(f => string.Equals(f.Id, fileId, StringComparison.Ordinal));
        if (file is null)
        {
            if (listing.UnreadableEntries.Any(e => string.Equals(e.Id, fileId, StringComparison.Ordinal)))
                return new DigitalFileLinkResult(DigitalFileLinkStatus.FileUnreadable);
            return new DigitalFileLinkResult(listing.IsComplete
                ? DigitalFileLinkStatus.FileNotOffered
                : DigitalFileLinkStatus.ListingIncomplete);
        }

        var link = await _linkRepository.GetByIdAsync(catalogItemId, cancellationToken);
        if (link is null)
        {
            link = new CatalogItemDigitalFile(catalogItemId, file.Id, file.Name, file.SizeBytes);
            await _linkRepository.AddAsync(link, cancellationToken);
        }
        else
        {
            link.Relink(file.Id, file.Name, file.SizeBytes);
            await _linkRepository.UpdateAsync(link, cancellationToken);
        }

        return new DigitalFileLinkResult(DigitalFileLinkStatus.Linked, link);
    }

    /// <summary>
    /// Decides whether <paramref name="buyerId"/> may download the file linked to <paramref name="catalogItemId"/>
    /// through order <paramref name="orderId"/>.
    /// </summary>
    public async Task<DigitalDownloadAuthorization> AuthorizeDownloadAsync(int orderId, int catalogItemId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            return new DigitalDownloadAuthorization(DigitalDownloadStatus.OrderNotFound);

        if (!order.OrderItems.Any(i => i.ItemOrdered.CatalogItemId == catalogItemId))
            return new DigitalDownloadAuthorization(DigitalDownloadStatus.ItemNotInOrder);

        var link = await _linkRepository.GetByIdAsync(catalogItemId, cancellationToken);
        if (link is null)
            return new DigitalDownloadAuthorization(DigitalDownloadStatus.NoDigitalFile);

        return new DigitalDownloadAuthorization(DigitalDownloadStatus.Authorized, link);
    }
}
