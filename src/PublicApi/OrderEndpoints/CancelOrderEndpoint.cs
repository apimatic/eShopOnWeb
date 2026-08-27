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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up the
/// provider has not sent yet is cancelled there so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IAppLogger<CancelOrderEndpoint> _logger;

    public CancelOrderEndpoint(IOrderNotificationService orderNotificationService,
        IAppLogger<CancelOrderEndpoint> logger)
    {
        _orderNotificationService = orderNotificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderRepository);
            })
            .Produces<OrderStateChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status != OrderStatus.Placed && order.Status != OrderStatus.Dispatched)
        {
            return Results.Conflict(new { message = $"Order is already {order.Status}." });
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        try
        {
            await _orderNotificationService.NotifyOrderCancelledAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {order.Id} cancelled, but notification handling failed: {ex.Message}");
        }

        return Results.Ok(new OrderStateChangeResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
