using System.Threading.Tasks;
using BlazorShared.Authorization;
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

public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orders, http);
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(DispatchOrderRequest request, IShopperOrderService orders, HttpContext http)
    {
        await orders.DispatchAsync(request.OrderId, http.RequestAborted);
        return Results.Ok(new OrderStatusResponse { OrderId = request.OrderId, Status = "Dispatched" });
    }
}
