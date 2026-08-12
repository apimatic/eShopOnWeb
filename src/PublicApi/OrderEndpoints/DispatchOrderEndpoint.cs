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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a delivery follow-up
/// is queued with the provider for a few days later. Restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
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
                    return Results.Conflict(new { message = "A cancelled order cannot be dispatched." });
                if (order.Status == OrderStatus.Dispatched)
                    return Results.Conflict(new { message = "This order has already been dispatched." });

                order.Dispatch();
                await orderRepository.UpdateAsync(order, ct);

                await notifications.NotifyOrderDispatchedAsync(order, ct);

                return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}

public record OrderStatusResponse(int OrderId, string Status);
