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
/// Operator action: cancels an order, tells the shopper, and calls off any
/// provider-queued follow-up that has not yet gone out.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly INotificationService _notificationService;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, INotificationService notificationService)
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
        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict(new { message = $"Order {orderId} is already cancelled." });
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order);

        // Best-effort: a message that cannot be sent never fails the cancellation.
        await _notificationService.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStateChangeResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
