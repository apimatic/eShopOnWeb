using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStateResponse
{
    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders/{orderId}/dispatch — operator action. Marks the order dispatched, tells
/// the shopper it is on its way, and queues a follow-up with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<OrderStatusRecord> statusRepository,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var statusRecord = await statusRepository.FirstOrDefaultAsync(
                    new OrderStatusRecordByOrderIdSpecification(orderId), cancellationToken);
                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                if (statusRecord is null || order is null)
                    return Results.NotFound();

                try
                {
                    statusRecord.MarkDispatched();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }

                await statusRepository.UpdateAsync(statusRecord, cancellationToken);

                // Best-effort messaging: the order is dispatched regardless of send outcome.
                await notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

                return Results.Ok(new OrderStateResponse { OrderId = orderId, State = statusRecord.State.ToString() });
            })
            .Produces<OrderStateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
