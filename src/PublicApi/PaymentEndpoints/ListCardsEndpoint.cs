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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// GET /api/payment-methods — the signed-in shopper's saved cards (safe descriptions only).
/// </summary>
public class ListCardsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
                await HandleAsync(user, paymentService))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IPaymentService paymentService)
    {
        var buyerId = CallerId.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await paymentService.ListCardsAsync(buyerId);
        var dtos = cards.Select(c => new SavedCardDto
        {
            PaymentMethodId = c.Id,
            Brand = c.Brand,
            LastDigits = c.LastDigits,
            Expiry = c.Expiry,
            CardholderName = c.CardholderName,
            CreatedAt = c.CreatedAt
        }).ToList();
        return Results.Ok(dtos);
    }
}
