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
/// The signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public ListPaymentMethodsEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            (Delegate)(async (HttpContext httpContext) => await HandleAsync(httpContext)))
            .Produces<IReadOnlyList<SavedCardDto>>()
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var cards = await _paymentService.GetPaymentMethodsAsync(
            CallerIdentity.GetBuyerId(httpContext),
            httpContext.RequestAborted);

        return Results.Ok(cards);
    }
}
