using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model. The shopper is then told (by SMS) their order was placed — best
/// effort, so a messaging failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
                IOrderPlacementService placementService, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(request, user, placementService, notificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IOrderPlacementService placementService, IOrderNotificationService notificationService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request?.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }
        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Each item needs a valid catalog item id and a positive quantity." });
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity));

        Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order order;
        try
        {
            order = await placementService.PlaceOrderAsync(buyerId, lines);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        // Notifying is best effort and must never fail the order that was just placed.
        try
        {
            await notificationService.NotifyOrderPlacedAsync(order);
        }
        catch
        {
            // swallowed intentionally; the notification service logs, and the order still succeeds
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
