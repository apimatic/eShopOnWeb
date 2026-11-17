using System;
using System.Collections.Generic;
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

/// <summary>
/// The signed-in shopper's orders with their current payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public MyOrdersEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            (Delegate)(async (HttpContext httpContext) => await HandleAsync(httpContext)))
            .Produces<IReadOnlyList<OrderDto>>()
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var orders = await _paymentService.GetMyOrdersAsync(
            CallerIdentity.GetBuyerId(httpContext),
            httpContext.RequestAborted);

        return Results.Ok(orders);
    }
}
