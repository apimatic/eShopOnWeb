using System.Threading.Tasks;
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
    public OrderResponse Order { get; set; } = new();
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICheckoutPaymentService checkout) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ICheckoutPaymentService checkout)
    {
        var order = await checkout.FulfilAsync(request.OrderId, default);
        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        });
    }
}
