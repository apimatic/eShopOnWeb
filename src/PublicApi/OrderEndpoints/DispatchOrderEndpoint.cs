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
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a
/// delivery follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderApiService orderService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId) { CancellationToken = cancellationToken }, orderService);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderApiService orderService)
    {
        var order = await orderService.DispatchAsync(request.OrderId, request.CancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status
        });
    }
}
