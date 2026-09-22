using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; the shopper's held funds are
/// released (voided), so no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<PaymentView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        try
        {
            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();
            var result = await svc.CancelAsync(orderId, http.RequestAborted);
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}
