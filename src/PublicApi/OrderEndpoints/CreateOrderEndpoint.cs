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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shopper action. Places an order for one or more catalog items and takes the ordered quantities
/// off eShop's own stock. An order for more of an item than eShop holds is refused. Reuses the app's
/// existing Order / OrderItem model. Does not call Walmart; Walmart learns the new levels at the
/// next sync.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IRepository<CatalogItem> itemRepository, IUriComposer uriComposer)
    {
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                request ??= new CreateOrderRequest();
                request.BuyerId = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                return await HandleAsync(request, orderRepository);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Every order item must have a quantity greater than zero." });
        }

        // Sum quantities per catalog item so repeated lines validate against stock correctly.
        var requested = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requested.Keys.ToArray()));
        var byId = catalogItems.ToDictionary(c => c.Id);

        // Validate everything before mutating any stock, so a single bad line refuses the whole order.
        foreach (var (catalogItemId, quantity) in requested)
        {
            if (!byId.TryGetValue(catalogItemId, out var item))
            {
                return Results.BadRequest(new { message = $"Catalog item {catalogItemId} does not exist." });
            }

            if (quantity > item.StockQuantity)
            {
                return Results.BadRequest(new
                {
                    message = $"Insufficient stock for catalog item {catalogItemId}: {item.StockQuantity} available, {quantity} requested."
                });
            }
        }

        // Build order items from the request lines (preserving the caller's line structure) and take
        // the ordered quantities off eShop's stock.
        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        foreach (var (catalogItemId, quantity) in requested)
        {
            byId[catalogItemId].RemoveStock(quantity);
            await _itemRepository.UpdateAsync(byId[catalogItemId]);
        }

        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(request.BuyerId, shipToAddress, orderItems);
        order = await orderRepository.AddAsync(order);

        return Results.Ok(new CreateOrderResponse { OrderId = order.Id });
    }
}
