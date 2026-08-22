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

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderCheckoutService checkoutService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), checkoutService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderCheckoutService checkoutService)
    {
        var order = await checkoutService.FulfilAsync(request.OrderId, default);
        return Results.Ok(OrderResponseMapper.Map(order));
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}
