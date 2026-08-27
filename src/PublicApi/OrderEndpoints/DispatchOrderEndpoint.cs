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

public class OrderStatusChangeResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks an order dispatched, texts the shopper, and queues a delivery
/// follow-up with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<DispatchOrderEndpoint> _logger;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository,
        IOrderNotificationService notificationService,
        IAppLogger<DispatchOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
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
        if (order.Status != OrderStatus.Placed)
        {
            return Results.Conflict(new { error = $"Order {orderId} is {order.Status} and cannot be dispatched." });
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);

        try
        {
            await _notificationService.NotifyOrderDispatchedAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Dispatch notification for order {OrderId} failed; the dispatch succeeded: {Error}", orderId, ex.Message);
        }

        return Results.Ok(new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
