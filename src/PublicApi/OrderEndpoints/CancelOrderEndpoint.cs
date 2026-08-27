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
/// Operator action: cancels an order, texts the shopper, and calls off any delivery
/// follow-up that has not gone out yet.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<CancelOrderEndpoint> _logger;

    public CancelOrderEndpoint(IRepository<Order> orderRepository,
        IOrderNotificationService notificationService,
        IAppLogger<CancelOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<OrderStatusChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"Order {orderId} is already cancelled." });
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        try
        {
            await _notificationService.NotifyOrderCancelledAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cancellation notification for order {OrderId} failed; the cancellation succeeded: {Error}", orderId, ex.Message);
        }

        return Results.Ok(new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
