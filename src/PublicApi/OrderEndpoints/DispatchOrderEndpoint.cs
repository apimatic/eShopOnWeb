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
/// Marks an order dispatched (operator). The shopper is told it is on its way and a
/// delivery follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, orderRepository, notificationService, cancellationToken);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(DispatchOrderRequest request, IRepository<Order> orderRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null) throw new OrderNotFoundException(request.OrderId);

        order.MarkDispatched();
        await orderRepository.UpdateAsync(order, cancellationToken);

        // Notification failures never fail the dispatch.
        await notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return Results.Ok(new DispatchOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
