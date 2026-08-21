using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(orderId, operatorOrderService);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOperatorOrderService operatorOrderService)
    {
        var order = await operatorOrderService.DispatchAsync(orderId);
        return Results.Ok(new OrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(orderId, operatorOrderService);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOperatorOrderService operatorOrderService)
    {
        var order = await operatorOrderService.CancelAsync(orderId);
        return Results.Ok(new OrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
