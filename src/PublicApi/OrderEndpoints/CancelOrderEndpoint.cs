using System.Threading;
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
/// Cancels an order (operator). The shopper is told, and any delivery follow-up that has
/// not gone out yet is called off at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, cancellationToken);
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken cancellationToken)
    {
        var response = new OrderStatusResponse();

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Best-effort: a notification failure never fails the cancellation.
        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        return Results.Ok(response);
    }
}
