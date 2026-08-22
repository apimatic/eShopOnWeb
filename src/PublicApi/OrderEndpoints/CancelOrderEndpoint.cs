using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "Cancelled";
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), httpContext, service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(CancelOrderRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        await service.CancelAsync(request.OrderId, httpContext.RequestAborted);
        return Results.Ok(new CancelOrderResponse { OrderId = request.OrderId, Status = "Cancelled" });
    }
}
