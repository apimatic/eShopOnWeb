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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a
/// delivery follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IAppLogger<DispatchOrderEndpoint> _logger;

    public DispatchOrderEndpoint(IOrderNotificationService orderNotificationService,
        IAppLogger<DispatchOrderEndpoint> logger)
    {
        _orderNotificationService = orderNotificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orderRepository);
            })
            .Produces<OrderStateChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status != OrderStatus.Placed)
        {
            return Results.Conflict(new { message = $"Only a placed order can be dispatched. Order is {order.Status}." });
        }

        order.MarkDispatched();
        await orderRepository.UpdateAsync(order);

        try
        {
            await _orderNotificationService.NotifyOrderDispatchedAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {order.Id} dispatched, but notification handling failed: {ex.Message}");
        }

        return Results.Ok(new OrderStateChangeResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderStateChangeResponse : BaseResponse
{
    public OrderStateChangeResponse(Guid correlationId) : base(correlationId) {}
    public OrderStateChangeResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
