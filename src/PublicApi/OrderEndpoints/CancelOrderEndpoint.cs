using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — an operator cancels the order. The shopper is told, and any follow-up that
/// has not yet gone out is called off so a cancelled order never prompts a "how did your delivery go?" message.
/// Operator action: restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, OrderEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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

        // Throws OrderStatusException (-> 409) if the order was already cancelled.
        order.Cancel();
        await services.Orders.UpdateAsync(order);

        // Call off any not-yet-sent follow-up, then tell the shopper. A messaging failure must not fail the cancel.
        await services.Notifier.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
