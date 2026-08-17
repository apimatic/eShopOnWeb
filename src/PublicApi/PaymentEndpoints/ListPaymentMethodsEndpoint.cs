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

/// <summary>GET /api/payment-methods — the caller's saved cards. Shopper-scoped.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, CallerOnlyRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                return await HandleAsync(new CallerOnlyRequest { Caller = CallerContext.From(user) }, service);
            })
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(CallerOnlyRequest request, IPaymentMethodService service)
    {
        var cards = await service.ListAsync(request.Caller.Username);
        return Results.Ok(cards);
    }
}
