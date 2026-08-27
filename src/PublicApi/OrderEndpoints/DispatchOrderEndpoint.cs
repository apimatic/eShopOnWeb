using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Marks an order dispatched (operator). Notifies the shopper and queues the
/// delivery follow-up message with the provider.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IOrderService orderService, IOrderNotificationService notificationService)
    {
        _orderService = orderService;
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
            .Produces<OrderStatusChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var order = await _orderService.DispatchOrderAsync(orderId);

        await _notificationService.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new OrderStatusChangeResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class OrderStatusChangeResponse : BaseResponse
{
    public OrderStatusChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderStatusChangeResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
