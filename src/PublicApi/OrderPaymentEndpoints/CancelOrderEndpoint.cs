using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action: cancel before fulfilment, releasing
/// the held funds so no money ever moves. Administrators only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(new OrderIdRequest(orderId), service))
            .Produces<OrderView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelOrderAsync(request.OrderId);
        return Results.Ok(OrderView.From(order));
    }
}
