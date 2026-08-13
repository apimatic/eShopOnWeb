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
/// Operator action: cancels an order. The shopper is told, and any follow-up not yet gone out is
/// called off so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, INotificationService notifications) =>
            {
                var order = await orderRepository.GetByIdAsync(orderId);
                if (order is null)
                    return Results.NotFound();

                if (order.Status == OrderStatus.Cancelled)
                    return Results.Conflict(new { message = "The order is already cancelled." });

                order.Cancel();
                await orderRepository.UpdateAsync(order);

                await notifications.NotifyOrderCancelledAsync(order);

                return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
