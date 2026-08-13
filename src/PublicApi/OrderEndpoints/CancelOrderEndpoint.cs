using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// POST /api/orders/{orderId}/cancel — an operator cancels the order. The shopper is told, and any
/// follow-up that has not yet gone out is called off so it never reaches them. Operator action:
/// restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var cancelled = await service.CancelAsync(orderId, cancellationToken);
                if (!cancelled)
                {
                    return Results.NotFound();
                }

                var notifications = await service.GetNotificationsForOrderAsync(orderId, refreshFromProvider: false, cancellationToken);
                return Results.Ok(new OrderActionResponse
                {
                    OrderId = orderId,
                    Message = "Order cancelled.",
                    Notifications = notifications?.Select(NotificationDto.From).ToList() ?? new List<NotificationDto>()
                });
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
