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

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService savedCardService) =>
                await HandleAsync(user.GetBuyerId(), savedCardService))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedPaymentMethodService savedCardService)
    {
        var cards = await savedCardService.ListAsync(buyerId);
        return Results.Ok(cards.Select(PaymentDtoMapper.ToDto).ToList());
    }
}
