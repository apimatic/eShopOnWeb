using System;
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

public class OrderStatusResponse : BaseResponse
{
    public OrderStatusResponse(Guid correlationId) : base(correlationId) { }
    public OrderStatusResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// An operator marks an order dispatched. The shopper is told it is on its way, and a delivery
/// follow-up is queued with the provider for a few days later. Administrator role only.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notifications) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
                if (order is null)
                    return Results.NotFound();

                // An invalid transition (e.g. dispatching a cancelled order) throws and is mapped to 409.
                order.MarkDispatched();
                await orderRepository.UpdateAsync(order);

                // Tell the shopper it's on its way and queue the delivery follow-up. Best-effort.
                await notifications.NotifyOrderDispatchedAsync(order);

                return Results.Ok(new OrderStatusResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<OrderStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
