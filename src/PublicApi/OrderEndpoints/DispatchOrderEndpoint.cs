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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a delivery-feedback
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository, IOrderNotificationService notifications, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, orderRepository, notifications, ct);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int orderId,
        IReadRepository<Order> orderRepository,
        IOrderNotificationService notifications,
        CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        // Best-effort messaging — the dispatch itself always succeeds.
        await notifications.NotifyOrderDispatchedAsync(order, ct);

        return Results.Ok(new OrderActionResponse { OrderId = order.Id, Status = "Dispatched" });
    }
}

public class OrderActionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
