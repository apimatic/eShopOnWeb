using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/dispatch — an operator marks the order dispatched. The shopper is told
/// it is on its way, and a delivery follow-up is queued with the provider for a few days later.
/// Restricted to administrators.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, INotificationService service) =>
            {
                try
                {
                    var order = await service.DispatchOrderAsync(orderId);
                    if (order is null)
                    {
                        return Results.NotFound();
                    }
                    return Results.Ok(new OrderActionResponse { OrderId = order.Id, Status = order.Status.ToString() });
                }
                catch (InvalidOperationException ex)
                {
                    // Invalid lifecycle transition (e.g. already dispatched, or cancelled).
                    return Results.Conflict(new { error = ex.Message });
                }
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
