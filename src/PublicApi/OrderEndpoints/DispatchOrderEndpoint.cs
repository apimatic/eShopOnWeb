using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/dispatch — an operator marks the order dispatched. The shopper is told it is on its
/// way, and a follow-up asking how the delivery went is queued with the provider for a few days later. Operator
/// action: restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, OrderEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderEndpointServices services) => await HandleAsync(orderId, services))
            .Produces<OrderStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, OrderEndpointServices services)
    {
        var order = await services.Orders.GetByIdAsync(orderId);
        if (order is null)
            return Results.NotFound();

        // Throws OrderStatusException (-> 409) if the transition is not allowed.
        order.Dispatch();
        await services.Orders.UpdateAsync(order);

        // Notify + queue the follow-up. A messaging failure must not fail the dispatch.
        await services.Notifier.NotifyOrderDispatchedAsync(order);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
