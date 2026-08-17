using System.Collections.Generic;
using System.Security.Claims;
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
/// GET /api/my-orders — the caller's own orders with their payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, CallerOnlyRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new CallerOnlyRequest { Caller = CallerContext.From(user) }, paymentService);
            })
            .Produces<IReadOnlyList<OrderPaymentView>>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CallerOnlyRequest request, IPaymentService paymentService)
    {
        var orders = await paymentService.GetMyOrdersAsync(request.Caller.Username);
        return Results.Ok(orders);
    }
}
