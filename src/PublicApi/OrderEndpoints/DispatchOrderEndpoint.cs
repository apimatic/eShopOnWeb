using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: mark an order dispatched. The shopper is told it is on its way, and a follow-up
/// asking how delivery went is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var orderRepository = httpContext.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Results.NotFound();

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        await orderRepository.UpdateAsync(order, cancellationToken);
        await notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return Results.Ok(new OrderActionResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
