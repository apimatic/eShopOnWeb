using System;
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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and
/// a delivery follow-up message is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, OrderStatusChangeRequest>
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
            order.MarkDispatched();
        }
        catch (InvalidOrderTransitionException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        await _orderRepository.UpdateAsync(order);
        await _notificationService.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new OrderStatusChangeResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class OrderStatusChangeRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderStatusChangeResponse : BaseResponse
{
    public OrderStatusChangeResponse(Guid correlationId) : base(correlationId) { }
    public OrderStatusChangeResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
