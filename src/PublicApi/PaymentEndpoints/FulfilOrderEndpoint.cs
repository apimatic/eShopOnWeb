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
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; the held funds are captured now.
/// A stale authorization is renewed first; one that cannot be renewed is reported in operator terms.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
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
            var result = await svc.FulfilAsync(orderId, http.RequestAborted);
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}
