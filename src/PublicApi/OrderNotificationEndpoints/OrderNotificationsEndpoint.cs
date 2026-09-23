using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record OrderNotificationsRequest(int OrderId, string? BuyerId);
public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationView> Notifications);

/// <summary>What was sent for one of the caller's orders and what became of each message. Each entry carries
/// its own notificationId (what the operator endpoints act on). A shopper sees only their own order.</summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new OrderNotificationsRequest(orderId, user.FindFirstValue(ClaimTypes.Name)), service);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var notifications = await service.GetOrderNotificationsAsync(request.OrderId, request.BuyerId, default);
        return Results.Ok(new OrderNotificationsResponse(request.OrderId, notifications));
    }
}
