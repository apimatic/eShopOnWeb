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
/// Cancels an order (operator). Notifies the shopper and calls off any
/// follow-up message that has not yet gone out.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IOrderService orderService, IOrderNotificationService notificationService)
    {
        _orderService = orderService;
        _notificationService = notificationService;
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
        var order = await _orderService.CancelOrderAsync(orderId);

        await _notificationService.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStatusChangeResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
