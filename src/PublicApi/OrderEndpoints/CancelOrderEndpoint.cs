using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels an order. Any not-yet-sent delivery
/// follow-up is called off first so it can never reach the customer, then the shopper is told.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var orderService = http.RequestServices.GetRequiredService<IApiOrderService>();
        var notificationService = http.RequestServices.GetRequiredService<ISmsNotificationService>();

        try
        {
            var order = await orderService.CancelAsync(orderId, http.RequestAborted);
            if (order is null)
            {
                return Results.NotFound();
            }

            var notifications = await notificationService.GetOrderNotificationsAsync(orderId, refresh: true, http.RequestAborted);
            return Results.Ok(OrderDto.From(order, notifications.Select(NotificationDto.From)));
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
