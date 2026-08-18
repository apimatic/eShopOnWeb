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
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing Order/OrderItem model. The shopper is told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    // The API request carries only items; orders still need a ship-to address, so we record a
    // placeholder (the storefront checkout does likewise for this sample).
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<CatalogItem> catalogRepository,
             IUriComposer uriComposer, IOrderNotificationService notificationService) =>
                await HandleAsync(request, user, orderRepository, catalogRepository, uriComposer, notificationService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.Problem("An order must contain at least one item.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.Problem("Every item must have a quantity of at least one.", statusCode: StatusCodes.Status400BadRequest);
        }

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await catalogRepository.ListAsync(new CatalogItemsSpecification(requestedIds));
        var missing = requestedIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Results.Problem($"Unknown catalog item(s): {string.Join(", ", missing)}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var orderItems = request.Items.Select(requestedItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requestedItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestedItem.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await orderRepository.AddAsync(order);

        // Best-effort: a message that cannot be sent must never fail placing the order.
        try
        {
            await notificationService.NotifyOrderPlacedAsync(order.Id, buyerId);
        }
        catch
        {
            // Swallowed deliberately — notification is best-effort and non-blocking.
        }

        var response = new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total());
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public record CreateOrderItemRequest(int CatalogItemId, int Quantity);

public record CreateOrderRequest(List<CreateOrderItemRequest> Items);

/// <summary>Carries the new order's identifier as a top-level field.</summary>
public record CreateOrderResponse(int OrderId, string Status, decimal Total);
