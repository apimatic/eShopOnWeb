using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Cancels an order (operator). The shopper is told, and any delivery
/// follow-up that has not yet gone out is called off at the provider.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, orderRepository);
            })
            .Produces<UpdateOrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            order.Cancel();
        }
        catch (InvalidOrderStatusTransitionException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }

        await orderRepository.UpdateAsync(order);

        // Never fails the cancellation: a messaging problem is recorded, not thrown.
        await _notificationService.NotifyOrderCancelledAsync(order);

        return Results.Ok(new UpdateOrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
