using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the signed-in shopper's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest,
    (IRepository<SavedPaymentMethod> SavedCards, ClaimsPrincipal User)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<SavedPaymentMethod> savedCards, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), (savedCards, user));
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request,
        (IRepository<SavedPaymentMethod> SavedCards, ClaimsPrincipal User) dependency)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await dependency.SavedCards.ListAsync(new SavedPaymentMethodsByBuyerIdSpec(buyerId));
        response.PaymentMethods = cards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.Id,
            CardBrand = c.CardBrand,
            Last4 = c.Last4,
            Expiry = c.Expiry,
            CardholderName = c.CardholderName
        }).ToList();

        return Results.Ok(response);
    }
}
