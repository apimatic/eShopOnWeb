using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService payments) =>
            {
                return await HandleAsync(orderId, payments);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService payments)
    {
        var order = await payments.CancelAsync(orderId, default);
        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Order = OrderPaymentDto.From(order)
        });
    }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}
