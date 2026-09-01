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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsResponse, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCardService;

    public ListPaymentMethodsEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListPaymentMethodsResponse(), user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsResponse response, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var savedCards = await _savedCardService.ListSavedCardsAsync(buyerId, CancellationToken.None);
        response.PaymentMethods = savedCards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.Id,
            Brand = c.Brand,
            LastDigits = c.LastDigits,
            Expiry = c.Expiry,
            CreatedAt = c.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
