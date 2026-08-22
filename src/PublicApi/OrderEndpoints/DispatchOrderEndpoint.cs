using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
}

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "Dispatched";
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), httpContext, service);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(DispatchOrderRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        await service.DispatchAsync(request.OrderId, httpContext.RequestAborted);
        return Results.Ok(new DispatchOrderResponse { OrderId = request.OrderId, Status = "Dispatched" });
    }
}
