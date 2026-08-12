using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator cancels an order: the shopper is told, and any delivery follow-up that has not yet gone
/// out is called off so a cancelled order never gets a "how did delivery go?". Administrator-only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IStoreOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IStoreOrderService orderService) =>
            {
                return await HandleAsync(orderId, orderService);
            })
            .Produces<OrderTransitionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IStoreOrderService orderService)
    {
        try
        {
            var order = await orderService.CancelOrderAsync(orderId);
            if (order is null)
                return Results.NotFound();

            return Results.Ok(new OrderTransitionResponse { OrderId = order.Id, Status = order.Status.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            // e.g. cancelling an already-cancelled order.
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
