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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the caller's orders, and what became of each message. Each entry carries
/// its own notificationId (what the operator endpoints act on). Refreshes live outcomes from the provider.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user, HttpContext http) =>
            {
                var callerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(callerId))
                {
                    return Results.Unauthorized();
                }

                var notifications = await service.GetOrderNotificationsAsync(callerId, orderId, http.RequestAborted);
                if (notifications is null)
                {
                    return Results.NotFound(); // not the caller's order (or none)
                }

                return Results.Ok(new OrderNotificationsResponse { OrderId = orderId, Notifications = notifications.ToList() });
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Empty as IResult);
}
