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
/// Operator action: cancels an order. The shopper is told, and any follow-up that has not yet gone out
/// is called off with the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service) => await HandleAsync(orderId, service))
            .Produces<OrderStateChangeResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IShopperOrderService service)
    {
        try
        {
            var order = await service.CancelAsync(orderId);
            if (order is null)
                return Results.NotFound();

            return Results.Ok(new OrderStateChangeResponse { OrderId = order.Id, Status = order.Status.ToString() });
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
