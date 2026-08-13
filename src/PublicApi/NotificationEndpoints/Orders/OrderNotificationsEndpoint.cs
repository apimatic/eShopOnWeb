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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Orders;

/// <summary>
/// Lists what was sent for one of the caller's own orders and what became of each message. Each entry
/// carries its own notificationId — what the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                return await HandleAsync(new OrderNotificationsRequest(orderId, buyerId), service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId, request.BuyerId);
        if (notifications is null)
        {
            // Either the order does not exist or it does not belong to the caller.
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(n => n.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}

public record OrderNotificationsRequest(int OrderId, string BuyerId);

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
