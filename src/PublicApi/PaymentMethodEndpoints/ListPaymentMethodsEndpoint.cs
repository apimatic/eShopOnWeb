using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<SavedCard> savedCardRepository) =>
            {
                return await HandleAsync(user, savedCardRepository);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<SavedCard> savedCardRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId));

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.FromSavedCard).ToList()
        };
        return Results.Ok(response);
    }
}
