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
/// Returns what was sent for an order and what became of each message. Shopper-scoped: the caller
/// must own the order. Each entry carries its own notificationId (what operator endpoints act on).
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, string, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(orderId, user.Identity!.Name!, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, string buyerId, IOrderNotificationService service)
    {
        var result = await service.GetOrderNotificationsAsync(orderId, buyerId);
        if (!result.IsSuccess)
            return result.ToFailureResult();

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = result.Value.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
