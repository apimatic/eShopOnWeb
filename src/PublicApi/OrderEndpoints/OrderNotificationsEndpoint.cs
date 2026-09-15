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
/// Returns what was sent for an order and what became of each message. Scoped to the order's owner;
/// an operator (administrator) may read any order. Each entry carries its own notificationId — what
/// the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var request = new OrderNotificationsRequest(orderId);
                request.SetCaller(user);
                return await HandleAsync(request, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        var order = await service.GetOrderAsync(request.OrderId);
        // A shopper may only see their own order; not revealing existence to others. Operators may see any.
        if (order is null || (!request.CallerIsAdmin && order.BuyerId != request.CallerUserName))
        {
            return Results.NotFound();
        }

        var notifications = await service.GetOrderNotificationsAsync(request.OrderId);
        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };

        return Results.Ok(response);
    }
}
