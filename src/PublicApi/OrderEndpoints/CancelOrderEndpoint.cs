using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. Any not-yet-sent delivery follow-up is called off with
/// the provider first, so it can never reach the shopper, and then the shopper is told the
/// order was cancelled. A messaging failure never fails the cancellation.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IOrderNotificationService notifications) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
                if (order is null)
                {
                    return Results.NotFound();
                }

                if (!order.Cancel())
                {
                    return Results.Conflict(new { message = $"Order {orderId} is already cancelled." });
                }

                await orderRepository.UpdateAsync(order);

                try
                {
                    await notifications.NotifyOrderCancelledAsync(order);
                }
                catch
                {
                    // Swallowed by design; the notification service logs the detail internally.
                }

                return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
