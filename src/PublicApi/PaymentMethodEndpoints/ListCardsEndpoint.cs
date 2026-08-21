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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListCardsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// GET /api/payment-methods — the caller's saved cards.
/// </summary>
public class ListCardsEndpoint : IEndpoint<IResult, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService, ClaimsPrincipal user) =>
                await HandleAsync(savedCardService, user))
            .Produces<ListCardsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService, ClaimsPrincipal user)
    {
        var ownerId = CallerIdentity.BuyerId(user);
        var cards = await savedCardService.GetCardsAsync(ownerId);
        return Results.Ok(new ListCardsResponse { PaymentMethods = cards.Select(PaymentMapper.ToDto).ToList() });
    }
}
