using System;
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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off at the provider so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(orderId, orderRepository, notificationService);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
            return Results.NotFound();

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }

        await orderRepository.UpdateAsync(order);
        await notificationService.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
