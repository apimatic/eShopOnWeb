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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has
/// not yet gone out is called off so it can never reach them. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, orderRepository, notificationService))
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        int orderId,
        IRepository<Order> orderRepository,
        IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOrderStatusTransitionException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        await orderRepository.UpdateAsync(order);

        // Best-effort: a message that cannot be sent must never fail the cancellation. Calling
        // off a still-pending delivery follow-up, however, is the core safety behaviour here.
        try
        {
            await notificationService.NotifyOrderCancelledAsync(order.Id, order.BuyerId);
        }
        catch
        {
            // Swallowed deliberately — notification is best-effort and non-blocking.
        }

        return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
    }
}
