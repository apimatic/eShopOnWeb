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

public class UpdateOrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Marks an order dispatched (operator). The shopper is told it is on its way
/// and a delivery follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, orderRepository);
            })
            .Produces<UpdateOrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOrderStatusTransitionException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }

        await orderRepository.UpdateAsync(order);

        // Never fails the dispatch: a messaging problem is recorded, not thrown.
        await _notificationService.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new UpdateOrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
