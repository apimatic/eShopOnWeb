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
/// What was sent for one of the caller's orders, and what became of each message. Each entry carries
/// its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
                await HandleAsync(new GetOrderNotificationsRequest(orderId), user, service))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetOrderNotificationsAsync(buyerId, request.OrderId);
        if (result is null)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = result.Order.Id,
            Notifications = result.Notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
