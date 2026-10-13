using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One line of an order: a catalog item and how many units.</summary>
public record CreateOrderItemRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

/// <summary>Body of <c>POST /api/orders</c>: the catalog items and quantities to order.</summary>
public record CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; init; } = new();

    /// <summary>Set from the caller's token, not the request body.</summary>
    public string BuyerId { get; init; } = string.Empty;
}

public record CreateOrderResponse(int OrderId);

/// <summary>
/// POST /api/orders — places an order (reusing eShop's Order/OrderItem model) and takes the ordered
/// quantities off eShop's own stock. Available to any signed-in shopper. An order for more of an item than
/// eShop holds is refused. Does not call Walmart; Walmart learns the new levels at the next sync.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    // No basket here, and the task specifies only ids + quantities, so ship to a placeholder address.
    private static readonly Address PlaceholderShipToAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository) =>
            {
                var buyerId = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown-buyer";
                return await HandleAsync(request with { BuyerId = buyerId }, orderRepository, itemRepository);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every order item must have a quantity greater than zero.");
        }

        // Collapse duplicate lines for the same catalog item so stock checks see the true demand.
        var demand = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var items = new Dictionary<int, CatalogItem>();
        foreach (var catalogItemId in demand.Keys)
        {
            var item = await itemRepository.GetByIdAsync(catalogItemId);
            if (item is null)
            {
                return Results.BadRequest($"Catalog item {catalogItemId} does not exist.");
            }
            items[catalogItemId] = item;
        }

        // Refuse the whole order if any line asks for more than eShop holds — before mutating any stock.
        foreach (var (catalogItemId, quantity) in demand)
        {
            if (quantity > items[catalogItemId].AvailableStock)
            {
                return Results.BadRequest(
                    $"Insufficient stock for catalog item {catalogItemId}: requested {quantity}, available {items[catalogItemId].AvailableStock}.");
            }
        }

        // Apply: take the ordered quantities off eShop stock and build the order items.
        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in demand)
        {
            var item = items[catalogItemId];
            item.RemoveStock(quantity);
            await itemRepository.UpdateAsync(item);

            var itemOrdered = new CatalogItemOrdered(
                item.Id,
                item.Name,
                string.IsNullOrEmpty(item.PictureUri) ? "placeholder.png" : item.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, item.Price, quantity));
        }

        var order = new Order(request.BuyerId, PlaceholderShipToAddress, orderItems);
        await orderRepository.AddAsync(order);

        return Results.Ok(new CreateOrderResponse(order.Id));
    }
}
