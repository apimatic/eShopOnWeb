using System;
using System.Collections.Generic;
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

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, System.Threading.CancellationToken ct) => await HandleAsync(http))
            .Produces<IReadOnlyList<PaymentView>>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        try
        {
            var buyerId = PaymentEndpointHelpers.GetBuyerId(http);
            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();
            var orders = await svc.GetMyOrdersAsync(buyerId, http.RequestAborted);
            return Results.Ok(orders);
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}
