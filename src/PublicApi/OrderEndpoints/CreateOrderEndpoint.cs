using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    /// <summary>Set from the caller's token identity, not from the request body.</summary>
    public string? BuyerId { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
}

/// <summary>
/// Any signed-in shopper: places an order from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model, and takes the ordered quantities off eShop's own stock. An
/// order for more of an item than eShop holds is refused. Does not call Walmart.
/// </summary>
public class CreateOrderEndpoint
    : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    // A ship-to address is required by the Order aggregate but is not part of this API's input;
    // use a fixed placeholder (as the storefront checkout does). The point here is the stock
    // decrement and the resulting order id.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
             ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                request.BuyerId = user?.Identity?.Name;
                return await HandleAsync(request, orderRepository, itemRepository, cancellationToken);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(
        CreateOrderRequest request, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository) =>
        HandleAsync(request, orderRepository, itemRepository, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every order item quantity must be greater than zero.");
        }

        // Fold duplicate lines for the same catalog item so stock is checked against the total.
        var requested = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var items = await itemRepository.ListAsync(
            new CatalogItemsSpecification(requested.Keys.ToArray()), cancellationToken);
        var itemsById = items.ToDictionary(i => i.Id);

        var missing = requested.Keys.Where(id => !itemsById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        // Refuse the whole order if any line exceeds eShop's held stock — before mutating anything.
        var insufficient = requested
            .Where(kvp => kvp.Value > itemsById[kvp.Key].AvailableStock)
            .Select(kvp => $"{kvp.Key} (requested {kvp.Value}, available {itemsById[kvp.Key].AvailableStock})")
            .ToList();
        if (insufficient.Count > 0)
        {
            return Results.BadRequest($"Insufficient stock for: {string.Join("; ", insufficient)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in requested)
        {
            var catalogItem = itemsById[catalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));

            catalogItem.RemoveStock(quantity);
            await itemRepository.UpdateAsync(catalogItem, cancellationToken);
        }

        var buyerId = string.IsNullOrWhiteSpace(request.BuyerId) ? "unknown-buyer" : request.BuyerId!;
        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await orderRepository.AddAsync(order, cancellationToken);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
        });
    }
}
