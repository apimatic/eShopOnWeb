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
/// Cancels an order (operator). The shopper is told, and any delivery follow-up that has
/// not yet gone out is cancelled at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderRepository, notificationService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = "The order is already cancelled." });
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        await notificationService.NotifyOrderCancelledAsync(order);

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Ok(response);
    }
}
