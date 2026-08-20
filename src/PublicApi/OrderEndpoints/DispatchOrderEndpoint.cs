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

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderFlowService orders) =>
            {
                return await HandleAsync(orderId, orders, http.RequestAborted);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderFlowService orders) =>
        HandleAsync(orderId, orders, default);

    private static async Task<IResult> HandleAsync(int orderId, IOrderFlowService orders, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var order = await orders.DispatchAsync(orderId, cancellationToken);
            return Results.Ok(new DispatchOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (System.Exception ex)
        {
            return EndpointErrors.FromException(ex);
        }
    }
}
