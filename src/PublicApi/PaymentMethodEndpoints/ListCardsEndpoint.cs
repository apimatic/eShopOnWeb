using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListCardsEndpoint : IEndpoint
{
    private readonly IReadRepository<SavedCard> _cardRepo;

    public ListCardsEndpoint(IReadRepository<SavedCard> cardRepo)
    {
        _cardRepo = cardRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(buyerId, ctx.RequestAborted);
            })
            .Produces<ListCardsResponse>(200)
            .WithTags("PaymentMethodEndpoints");
    }

    private async Task<IResult> HandleAsync(string buyerId, System.Threading.CancellationToken ct)
    {
        var spec = new SavedCardsByBuyerIdSpec(buyerId);
        var cards = await _cardRepo.ListAsync(spec, ct);

        var dtos = new List<SavedCardDto>();
        foreach (var card in cards)
        {
            dtos.Add(new SavedCardDto
            {
                PaymentMethodId = card.Id,
                Last4 = card.Last4,
                Brand = card.CardBrand,
                Expiry = card.Expiry,
                SavedAt = card.SavedAt
            });
        }

        return Results.Ok(new ListCardsResponse { PaymentMethods = dtos });
    }
}

public class ListCardsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset SavedAt { get; set; }
}
