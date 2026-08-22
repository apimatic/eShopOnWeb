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

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICheckoutService checkout) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ICheckoutService checkout)
    {
        var order = await checkout.FulfilAsync(request.OrderId, default);
        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        });
    }
}
