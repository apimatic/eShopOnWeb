using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Operator action: cancel before fulfilment — release the held funds (no money moves).</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new OrderActionRequest { OrderId = orderId }, service, http))
            .Produces<OrderView>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service, HttpContext http)
    {
        var view = await service.CancelAsync(request.OrderId, http.RequestAborted);
        return Results.Ok(view);
    }
}
