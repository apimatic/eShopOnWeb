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
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for one order and what became of each message. Each entry carries its own
/// notificationId — the handle the operator endpoints act on. A shopper sees only their own order;
/// an operator may see any.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var (outcome, notifications) = await service.GetOrderNotificationsAsync(
                    orderId, user.GetBuyerId(), user.IsAdministrator());

                return outcome switch
                {
                    AccessOutcome.NotFound => Results.NotFound(),
                    // Explicit 403 — Results.Forbid() would invoke Identity's cookie forbid handler (302).
                    AccessOutcome.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                    _ => Results.Ok(new OrderNotificationsResponse
                    {
                        OrderId = orderId,
                        Notifications = notifications.Select(NotificationDto.From).ToList()
                    })
                };
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService service) =>
        Task.FromResult(Results.Ok());
}
