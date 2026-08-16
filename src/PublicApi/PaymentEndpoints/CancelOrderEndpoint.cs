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
/// POST /api/orders/{orderId}/cancel — operator action. Cancels before fulfilment, voiding the
/// authorization so the shopper's held funds are released and no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) => await HandleAsync(orderId, paymentService))
            .Produces<OrderSummaryDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService paymentService)
    {
        var order = await paymentService.CancelAsync(orderId);
        return Results.Ok(PaymentDtoMapper.ToDto(order));
    }
}
