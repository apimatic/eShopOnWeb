using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the signed-in shopper's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, PaymentMethodsQuery, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct, ISavedCardService service) =>
            {
                return await HandleAsync(new PaymentMethodsQuery(user.GetUserId(), ct), service);
            })
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodsQuery request, ISavedCardService service)
    {
        var cards = await service.GetCardsAsync(request.UserId, request.Cancellation);
        return Results.Ok(cards.Select(SavedCardDto.From).ToList());
    }
}

public record PaymentMethodsQuery(string UserId, CancellationToken Cancellation);
