using System.Collections.Generic;
using System.Linq;
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
/// Lists what was sent for one of the caller's orders and what became of each message. Each entry
/// carries its own notificationId — what the operator endpoints act on. Shopper-scoped: a caller
/// can only see notifications for their own orders.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderActionRequest, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), user, service);
            })
            .Produces<List<NotificationDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        var order = await service.GetOrderAsync(request.OrderId);
        // Not found and not-yours are indistinguishable to the caller, by design.
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await service.ListNotificationsForOrderAsync(request.OrderId);
        return Results.Ok(notifications.Select(NotificationDto.From).ToList());
    }
}
