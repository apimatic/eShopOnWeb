using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any not-yet-sent follow-up
/// is called off at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderApiService orderService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId) { CancellationToken = cancellationToken }, orderService);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderApiService orderService)
    {
        var order = await orderService.CancelAsync(request.OrderId, request.CancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status
        });
    }
}
