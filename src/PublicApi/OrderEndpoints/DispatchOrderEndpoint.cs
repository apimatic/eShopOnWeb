using System;
using System.Security.Claims;
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
/// Operator action: marks an order dispatched, tells the shopper it is on its way,
/// and queues a delivery follow-up message with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly INotificationService _notificationService;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository, INotificationService notificationService)
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
                return await HandleAsync(orderId);
            })
            .Produces<OrderStateChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound();
        }
        if (order.Status != OrderStatus.Placed)
        {
            return Results.Conflict(new { message = $"Order {orderId} is {order.Status} and cannot be dispatched." });
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);

        // Best-effort: a message that cannot be sent never fails the dispatch.
        await _notificationService.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new OrderStateChangeResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class OrderStateChangeResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
