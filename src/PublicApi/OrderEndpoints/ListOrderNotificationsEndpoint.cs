using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for an order and what became of each message, refreshing the
/// delivery outcome from the provider. Accessible to the order's owner and to operators.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, orderRepository, notificationService, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository,
        IOrderNotificationService notificationService, ClaimsPrincipal user)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.BuyerId != user.GetUserName() && !user.IsAdministrator())
        {
            return Results.Forbid();
        }

        var notifications = await notificationService.GetOrderNotificationsAsync(orderId);
        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => new OrderNotificationDetailDto
            {
                NotificationId = n.Id,
                Type = n.Type.ToString(),
                Status = n.Status,
                ErrorMessage = n.ErrorMessage,
                CreatedAt = n.CreatedAt,
                ScheduledFor = n.ScheduledFor,
                ContentDisposed = n.ContentDisposed,
                Body = n.ContentDisposed ? null : n.Body,
                ProviderMessageSid = n.ProviderMessageSid
            }).ToList()
        };
        return Results.Ok(response);
    }
}
