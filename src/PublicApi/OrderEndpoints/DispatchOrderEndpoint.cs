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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a
/// "how did delivery go?" follow-up is queued with the provider for a few days later.
/// A messaging failure never fails the dispatch.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
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

                if (!order.Dispatch())
                {
                    return Results.Conflict(new { message = $"Order {orderId} cannot be dispatched from status {order.Status}." });
                }

                await orderRepository.UpdateAsync(order);

                try
                {
                    await notifications.NotifyOrderDispatchedAsync(order);
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

public class OrderStatusResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
