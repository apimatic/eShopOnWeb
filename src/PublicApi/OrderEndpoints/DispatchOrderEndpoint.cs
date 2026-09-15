using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IShopperOrderService _orderService;

    public DispatchOrderEndpoint(IShopperOrderService orderService)
    {
        _orderService = orderService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        await _orderService.DispatchAsync(orderId);
        return Results.Ok(new OrderActionResponse { OrderId = orderId, Status = "Dispatched" });
    }
}

public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
