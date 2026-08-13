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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a follow-up
/// asking how the delivery went is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, INotificationService notifications) =>
            {
                var order = await orderRepository.GetByIdAsync(orderId);
                if (order is null)
                    return Results.NotFound();

                if (order.Status != OrderStatus.Placed)
                    return Results.Conflict(new { message = $"Only a placed order can be dispatched (current status: {order.Status})." });

                order.Dispatch();
                await orderRepository.UpdateAsync(order);

                await notifications.NotifyOrderDispatchedAsync(order);

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
