using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. Any not-yet-sent delivery follow-up is called off so it can never
/// reach the shopper, and the shopper is told the order was cancelled.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
                await HandleAsync(orderId, service))
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        try
        {
            var order = await service.CancelAsync(orderId);
            var view = await service.GetOrderNotificationsAsync(orderId, order.BuyerId);

            return Results.Ok(new OrderActionResponse(System.Guid.NewGuid())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Notifications = (view?.Notifications ?? System.Array.Empty<Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification>())
                    .Select(NotificationDto.FromEntity).ToList()
            });
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
