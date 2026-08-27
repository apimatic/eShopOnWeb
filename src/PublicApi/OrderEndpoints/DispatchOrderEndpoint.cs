using System;
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

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId) { }
    public DispatchOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on
/// its way and a delivery follow-up is queued with the provider for a few
/// days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, orderRepository, notificationService, cancellationToken);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }
        if (order.Status != OrderStatus.Submitted)
        {
            return Results.Conflict(new { error = $"Order {orderId} is {order.Status} and cannot be dispatched." });
        }

        order.MarkDispatched();
        await orderRepository.UpdateAsync(order, cancellationToken);

        // Notification failures never fail the dispatch.
        await notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return Results.Ok(new DispatchOrderResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
