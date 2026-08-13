using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks an order dispatched, tells the shopper it is on its way, and queues a
/// "how did the delivery go?" follow-up with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
                await HandleAsync(orderId, orderRepository))
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        var ct = _httpContextAccessor.RequestAborted();

        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Problem("A cancelled order cannot be dispatched.", statusCode: StatusCodes.Status409Conflict);
        }

        // The underlying operation (marking dispatched) must succeed regardless of messaging.
        order.MarkDispatched();
        await orderRepository.UpdateAsync(order, ct);

        var notificationService = _httpContextAccessor.RequestService<IOrderNotificationService>();
        await notificationService.NotifyOrderDispatchedAsync(order, ct);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
