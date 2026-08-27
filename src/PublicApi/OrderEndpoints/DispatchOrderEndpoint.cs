using System;
using System.Threading.Tasks;
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
/// Operator action: marks an order dispatched. The shopper is told it is on its
/// way, and a delivery follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId));
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status != OrderStatus.Placed)
        {
            return Results.Conflict(new { error = $"Order {order.Id} is {order.Status}; only a placed order can be dispatched." });
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);

        // Best-effort: messaging problems must never fail the dispatch.
        await _notificationService.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
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

public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
