using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's orders and what became of each message. Each entry carries
/// its own notificationId, which the operator endpoints act on. Scoped to the caller's own orders.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(
                    new OrderNotificationsRequest { BuyerId = user.GetBuyerId(), OrderId = orderId }, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var notifications = await service.GetOrderNotificationsAsync(request.BuyerId, request.OrderId);
        // Null means the order does not exist or is not the caller's — do not reveal which.
        if (notifications is null) return Results.NotFound();

        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = new List<NotificationDto>(NotificationDto.From(notifications))
        };
        return Results.Ok(response);
    }
}
