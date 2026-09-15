using System;
using System.Security.Claims;
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
/// Operator action: cancels an order, tells the shopper, and calls off the not-yet-sent follow-up so
/// it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var request = new OrderActionRequest(orderId);
                request.SetCaller(user);
                return await HandleAsync(request, service);
            })
            .Produces<OrderStateResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderNotificationService service)
    {
        try
        {
            var order = await service.CancelAsync(request.OrderId);
            if (order is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new OrderStateResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
