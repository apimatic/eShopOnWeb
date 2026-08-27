using System.Threading;
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
/// Cancels an order (operator). The shopper is told, and any follow-up that has not
/// yet gone out is called off at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orderRepository, notificationService, cancellationToken);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) throw new OrderNotFoundException(request.OrderId);

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order, cancellationToken);

        // Notification failures never fail the cancellation.
        await notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
