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
/// Places an order for one or more catalog items, reusing eShop's existing order/order-item model.
/// The ordered quantities are taken off eShop's own stock; an order for more of an item than eShop
/// holds is refused. Available to any signed-in shopper. This does not call Walmart.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<CatalogItem>, IRepository<Order>>
{
    // The API carries only catalog item ids and quantities; orders still need a ship-to address, so a
    // placeholder is used for this operator/shopper API surface.
    private static readonly Address PlaceholderShipToAddress =
        new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<CatalogItem> itemRepository, IRepository<Order> orderRepository) =>
            {
                // Trust only the token for the buyer identity; never a client-supplied value.
                request.BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                return await HandleAsync(request, itemRepository, orderRepository);
            })
            .Produces<CreateOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every order item must have a quantity greater than zero.");
        }

        var buyerId = request.BuyerId;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Combine duplicate lines for the same item so stock is checked against the real demand.
        var requestedQuantities = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var catalogItems = await itemRepository.ListAsync(
            new CatalogItemsSpecification(requestedQuantities.Keys.ToArray()));
        var itemsById = catalogItems.ToDictionary(c => c.Id);

        // Validate everything before mutating any stock, so a refusal leaves eShop unchanged.
        var missing = requestedQuantities.Keys.Where(id => !itemsById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var insufficient = requestedQuantities
            .Where(kvp => kvp.Value > itemsById[kvp.Key].StockQuantity)
            .Select(kvp => $"item {kvp.Key} (requested {kvp.Value}, in stock {itemsById[kvp.Key].StockQuantity})")
            .ToList();
        if (insufficient.Count > 0)
        {
            return Results.BadRequest($"Insufficient eShop stock for: {string.Join("; ", insufficient)}.");
        }

        var orderItems = requestedQuantities.Select(kvp =>
        {
            var catalogItem = itemsById[kvp.Key];
            catalogItem.RemoveStock(kvp.Value);

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "no-image";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, PlaceholderShipToAddress, orderItems);

        // AddAsync saves on the shared CatalogContext, persisting the new order and the stock decrements.
        await orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id
        };
        return Results.Ok(response);
    }
}
