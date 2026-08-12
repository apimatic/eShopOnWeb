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
/// Operator marks an order dispatched: the shopper is told it is on its way and a delivery follow-up
/// is queued with the provider for a few days later. Administrator-only.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IStoreOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
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
            var order = await orderService.DispatchOrderAsync(orderId);
            if (order is null)
                return Results.NotFound();

            return Results.Ok(new OrderTransitionResponse { OrderId = order.Id, Status = order.Status.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            // e.g. dispatching a cancelled or already-dispatched order.
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
