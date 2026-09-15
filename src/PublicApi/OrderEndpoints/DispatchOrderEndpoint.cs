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
    public int OrderId { get; set; }

    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPlacementService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), service);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderPlacementService service)
    {
        var order = await service.DispatchAsync(request.OrderId);
        return Results.Ok(new DispatchOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
