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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderProcessingService orderProcessingService) =>
            {
                return await HandleAsync(orderId, orderProcessingService);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderProcessingService orderProcessingService)
    {
        var result = await orderProcessingService.CancelOrderAsync(orderId);

        if (!result.Found)
        {
            return Results.NotFound();
        }
        if (result.Error is not null)
        {
            return Results.Conflict(new { error = result.Error });
        }

        return Results.Ok(new OrderStatusResponse
        {
            OrderId = result.Order!.Id,
            Status = result.Order.Status.ToString()
        });
    }
}
