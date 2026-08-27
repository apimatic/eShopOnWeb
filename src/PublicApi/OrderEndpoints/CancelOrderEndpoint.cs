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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up
/// that has not yet gone out is called off at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderStatusChangeRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new OrderStatusChangeRequest { OrderId = orderId });
            })
            .Produces<OrderStatusChangeResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderStatusChangeRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOrderTransitionException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        await _orderRepository.UpdateAsync(order);
        await _notificationService.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStatusChangeResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
