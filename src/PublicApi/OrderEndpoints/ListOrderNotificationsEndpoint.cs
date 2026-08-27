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
/// Lists what was sent for an order and what became of each message. Shoppers see only
/// their own orders; operators see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(orderId, user, notificationService);
            })
            .Produces<OrderNotificationDto[]>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IOrderNotificationService notificationService)
    {
        var callerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var isOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, callerId, isOperator);
        if (notifications is null)
        {
            // Another shopper's order is indistinguishable from a missing one.
            return Results.NotFound();
        }

        return Results.Ok(notifications.Select(OrderNotificationDto.FromEntity).ToList());
    }
}
