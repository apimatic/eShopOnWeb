using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action: cancel before fulfilment, releasing any held
/// funds (voiding the authorization) so no money ever moves. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService service) => await HandleAsync(orderId, service))
            .Produces<OrderPaymentResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service)
    {
        var order = await service.CancelOrderAsync(orderId);
        return Results.Ok(order.ToResponse());
    }
}
