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
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICheckoutPaymentService checkout) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ICheckoutPaymentService checkout)
    {
        var order = await checkout.CancelAsync(request.OrderId, default);
        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        });
    }
}
