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
/// POST /api/orders/{orderId}/fulfil — operator action. Captures the held funds (money is taken).
/// A stale authorization is renewed first; one that can no longer be renewed is reported.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) => await HandleAsync(orderId, paymentService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService)
    {
        var result = await paymentService.FulfilOrderAsync(orderId);
        return PaymentApi.ToHttp(result, PaymentApi.PaymentDto);
    }
}
