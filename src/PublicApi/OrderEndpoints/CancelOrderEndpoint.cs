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
/// Cancels an order (operator action). The shopper is told, and any follow-up
/// message still queued with the provider is called off so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderStatusChangeRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new OrderStatusChangeRequest(orderId), orderRepository);
            })
            .Produces<OrderStatusChangeResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderStatusChangeRequest request, IRepository<Order> orderRepository)
    {
        var response = new OrderStatusChangeResponse(request.CorrelationId());

        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict($"Order {order.Id} is already cancelled.");
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        // Tells the shopper and calls off any queued follow-up. A failed message
        // never fails the cancellation.
        await _notificationService.NotifyOrderCancelledAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        return Results.Ok(response);
    }
}
