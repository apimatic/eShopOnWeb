using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>GET /api/payment-methods — the caller's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service, HttpContext ctx) =>
                await HandleAsync(service, ctx))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);
        var cards = await service.GetCardsAsync(buyerId, ctx.RequestAborted);
        var dtos = cards
            .Select(c => new SavedCardDto(c.Id, c.Describe(), c.CardBrand, c.CardLast4, c.CardExpiry, c.Alias, c.CreatedAt))
            .ToList();
        return Results.Ok(dtos);
    }
}

public record SavedCardDto(
    int PaymentMethodId, string Description, string? Brand, string? Last4, string? Expiry, string? Alias, DateTimeOffset CreatedAt);
