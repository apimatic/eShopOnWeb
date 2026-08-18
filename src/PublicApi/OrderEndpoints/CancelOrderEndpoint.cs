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
/// Operator action: cancels an order, tells the shopper, and calls off any delivery follow-up that
/// has not yet gone out so it never reaches them. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        var result = await service.CancelAsync(orderId);
        if (!result.IsSuccess)
            return result.ToFailureResult();

        return Results.Ok(new OrderTransitionResponse
        {
            OrderId = orderId,
            Status = OrderStatus.Cancelled.ToString()
        });
    }
}
