using System.Threading;
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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not yet gone
/// out is called off with the provider — a shopper is never asked how a delivery went for a cancelled order.
/// Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var order = await orderRepository.GetByIdAsync(orderId, ct);
                if (order is null)
                    return Results.NotFound();

                if (order.Status == OrderStatus.Cancelled)
                    return Results.Conflict(new { message = "This order has already been cancelled." });

                order.Cancel();
                await orderRepository.UpdateAsync(order, ct);

                await notifications.NotifyOrderCancelledAsync(order, ct);

                return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}
