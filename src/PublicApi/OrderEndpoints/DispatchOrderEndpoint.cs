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
/// POST /api/orders/{orderId}/dispatch — an operator marks the order dispatched. The shopper is told it
/// is on its way, and the delayed "how did the delivery go?" follow-up is queued with the provider.
/// Operator action: restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var dispatched = await service.DispatchAsync(orderId, cancellationToken);
                if (!dispatched)
                {
                    return Results.NotFound();
                }

                var notifications = await service.GetNotificationsForOrderAsync(orderId, refreshFromProvider: false, cancellationToken);
                return Results.Ok(new OrderActionResponse
                {
                    OrderId = orderId,
                    Message = "Order dispatched.",
                    Notifications = notifications?.Select(NotificationDto.From).ToList() ?? new List<NotificationDto>()
                });
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
