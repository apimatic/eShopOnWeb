using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CatalogStockService : ICatalogStockService
{
    private readonly IRepository<CatalogStockEntry> _stockRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public CatalogStockService(
        IRepository<CatalogStockEntry> stockRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _stockRepository = stockRepository;
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
    }

    public async Task SetStockAsync(int catalogItemId, int quantity, CancellationToken cancellationToken)
    {
        Guard.Against.Negative(quantity, nameof(quantity));

        var item = await _itemRepository.GetByIdAsync(catalogItemId, cancellationToken);
        if (item is null)
        {
            throw new InsufficientStockException($"Catalog item {catalogItemId} does not exist.");
        }

        var entry = await _stockRepository.FirstOrDefaultAsync(
            new CatalogStockEntryByCatalogItemIdSpecification(catalogItemId), cancellationToken);

        if (entry is null)
        {
            await _stockRepository.AddAsync(new CatalogStockEntry(catalogItemId, quantity), cancellationToken);
        }
        else
        {
            entry.SetQuantity(quantity);
            await _stockRepository.UpdateAsync(entry, cancellationToken);
        }
    }

    public async Task<int> GetStockAsync(int catalogItemId, CancellationToken cancellationToken)
    {
        var entry = await _stockRepository.FirstOrDefaultAsync(
            new CatalogStockEntryByCatalogItemIdSpecification(catalogItemId), cancellationToken);
        return entry?.Quantity ?? 0;
    }

    public async Task<IReadOnlyList<CatalogItemStockLevel>> GetAllStockAsync(CancellationToken cancellationToken)
    {
        var items = await _itemRepository.ListAsync(cancellationToken);
        var entries = await _stockRepository.ListAsync(cancellationToken);
        var levelByItem = entries.ToDictionary(e => e.CatalogItemId, e => e.Quantity);

        return items
            .Select(item => new CatalogItemStockLevel(
                item.Id,
                WalmartSku.ForCatalogItem(item.Id),
                levelByItem.TryGetValue(item.Id, out var qty) ? qty : 0))
            .ToList();
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new InsufficientStockException("An order must contain at least one item.");
        }

        // Combine repeated lines for the same item so a SKU asked for twice is checked as one total.
        var requested = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        foreach (var (catalogItemId, quantity) in requested)
        {
            if (quantity <= 0)
            {
                throw new InsufficientStockException(
                    $"Quantity for catalog item {catalogItemId} must be greater than zero.");
            }
        }

        var ids = requested.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var entries = await _stockRepository.ListAsync(cancellationToken);
        var entryByItem = entries.ToDictionary(e => e.CatalogItemId);

        // Validate everything BEFORE mutating anything, so a refused order changes no stock.
        var problems = new List<string>();
        foreach (var (catalogItemId, quantity) in requested)
        {
            if (catalogItems.All(c => c.Id != catalogItemId))
            {
                problems.Add($"catalog item {catalogItemId} does not exist");
                continue;
            }
            var available = entryByItem.TryGetValue(catalogItemId, out var e) ? e.Quantity : 0;
            if (quantity > available)
            {
                problems.Add($"catalog item {catalogItemId}: requested {quantity}, only {available} in stock");
            }
        }
        if (problems.Count > 0)
        {
            throw new InsufficientStockException("Order refused: " + string.Join("; ", problems) + ".");
        }

        // Draw down stock and build the order from the existing Order/OrderItem aggregate.
        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in requested)
        {
            var entry = entryByItem[catalogItemId];
            entry.Reduce(quantity);
            await _stockRepository.UpdateAsync(entry, cancellationToken);

            var catalogItem = catalogItems.First(c => c.Id == catalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        // POST /api/orders carries only item ids and quantities; the Order aggregate requires a
        // shipping address, so a placeholder is supplied.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shipToAddress, orderItems);

        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
