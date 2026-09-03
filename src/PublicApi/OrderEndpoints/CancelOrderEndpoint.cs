using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), httpContext, orderService);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        CancelOrderRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var response = new CancelOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };
        await orderService.CancelAsync(request.OrderId, httpContext.RequestAborted);
        return Results.Ok(response);
    }
}
