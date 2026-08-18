using System.Collections.Generic;
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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing Order/OrderItem
/// model. The caller's identity comes from the token (never the request body). Once placed, the shopper is told
/// their order was placed — best-effort, so a messaging problem never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IRepository<Order> orderRepository, IReadRepository<CatalogItem> catalogRepository,
             IOrderNotificationService notifications, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, orderRepository, catalogRepository, notifications, user, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IOrderNotificationService notifications,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.UserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one order item is required." });
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                return Results.BadRequest(new { message = $"Quantity for catalog item {line.CatalogItemId} must be greater than zero." });
            }

            var catalogItem = await catalogRepository.GetByIdAsync(line.CatalogItemId, ct);
            if (catalogItem is null)
            {
                return Results.BadRequest(new { message = $"Catalog item {line.CatalogItemId} does not exist." });
            }

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = request.ShipToAddress?.ToAddress() ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        await orderRepository.AddAsync(order, ct);

        // Best-effort notification — never fails the placement.
        await notifications.NotifyOrderPlacedAsync(order, ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
