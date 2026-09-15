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
/// An operator cancels an order. The shopper is told, and any delivery follow-up that has not yet gone
/// out is called off with the provider so it can never reach them. Administrator role only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notifications) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
                if (order is null)
                    return Results.NotFound();

                order.MarkCancelled();
                await orderRepository.UpdateAsync(order);

                // Call off any pending follow-up first, then tell the shopper. Best-effort.
                await notifications.NotifyOrderCancelledAsync(order);

                return Results.Ok(new OrderStatusResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<OrderStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
