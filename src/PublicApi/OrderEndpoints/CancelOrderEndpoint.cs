using System.Threading;
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
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orders, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, orders, cancellationToken);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderPaymentService orders)
    {
        return HandleAsync(orderId, orders, default);
    }

    private async Task<IResult> HandleAsync(int orderId, IOrderPaymentService orders, CancellationToken cancellationToken)
    {
        var order = await orders.CancelAsync(orderId, cancellationToken);
        var response = new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}
