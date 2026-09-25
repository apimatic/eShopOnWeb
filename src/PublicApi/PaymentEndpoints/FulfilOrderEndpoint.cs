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

/// <summary>Operator action: fulfil the order — capture the held funds (renewing a stale hold first).</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new OrderActionRequest { OrderId = orderId }, service, http))
            .Produces<OrderView>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service, HttpContext http)
    {
        var view = await service.FulfilAsync(request.OrderId, http.RequestAborted);
        return Results.Ok(view);
    }
}
