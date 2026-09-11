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
/// POST /api/orders/{orderId}/cancel — operator action. Cancel before fulfilment: the held funds
/// are released (the authorization is voided) so no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) => await HandleAsync(orderId, paymentService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService)
    {
        var result = await paymentService.CancelOrderAsync(orderId);
        return PaymentApi.ToHttp(result, payment => payment is null
            ? new { orderId, orderStatus = "Cancelled", payment = (object?)null }
            : new { orderId, orderStatus = "Cancelled", payment = (object?)PaymentApi.PaymentDto(payment) });
    }
}
