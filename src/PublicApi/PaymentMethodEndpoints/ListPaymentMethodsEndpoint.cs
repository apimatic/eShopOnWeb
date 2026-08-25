using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record PaymentMethodDto(string PaymentMethodId, string? LastFourDigits, string? CardBrand, string? Expiry, string? CardHolderName);
public record ListPaymentMethodsResponse(IReadOnlyList<PaymentMethodDto> PaymentMethods);

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService svc, IHttpContextAccessor ctx) => await HandleAsync(svc, ctx))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(IPaymentService svc, IHttpContextAccessor ctx)
    {
        var shopperId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        var cards = await svc.GetSavedCardsAsync(shopperId);
        var dtos = cards.Select(c => new PaymentMethodDto(
            c.PayPalPaymentTokenId, c.LastFourDigits, c.CardBrand, c.CardExpiry, c.CardHolderName)).ToList();

        return Results.Ok(new ListPaymentMethodsResponse(dtos));
    }
}
