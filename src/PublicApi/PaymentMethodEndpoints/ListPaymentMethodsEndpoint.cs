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

/// <summary>The caller's own saved cards. Never returns another buyer's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.Identity!.Name! }, savedCardService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService savedCardService)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var cards = await savedCardService.ListSavedCardsAsync(request.BuyerId, CancellationToken.None);

        response.PaymentMethods = cards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.Id,
            CardBrand = c.CardBrand,
            Last4 = c.Last4,
            Expiry = c.Expiry,
            CreatedAt = c.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
