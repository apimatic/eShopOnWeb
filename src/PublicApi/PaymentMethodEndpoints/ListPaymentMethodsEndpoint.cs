using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SavedCardDto(int PaymentMethodId, string Brand, string Last4, string Expiry, string? Label, DateTimeOffset CreatedAt);

public record ListPaymentMethodsResponse(IReadOnlyList<SavedCardDto> PaymentMethods);

/// <summary>
/// GET /api/payment-methods — the caller's saved cards. Shopper-scoped: only the caller's cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService) =>
                await HandleAsync(savedCardService))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        var cards = await savedCardService.ListAsync(buyerId);

        var dtos = cards
            .Select(c => new SavedCardDto(c.Id, c.Brand, c.Last4, c.Expiry, c.Label, c.CreatedAt))
            .ToList();

        return Results.Ok(new ListPaymentMethodsResponse(dtos));
    }
}
