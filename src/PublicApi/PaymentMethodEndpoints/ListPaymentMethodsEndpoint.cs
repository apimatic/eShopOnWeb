using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, IRepository<SavedCard> savedCardRepo) =>
            {
                var buyerId = ctx.User.Identity?.Name ?? string.Empty;
                var request = new ListPaymentMethodsRequest { BuyerId = buyerId };
                return await HandleAsync(request, savedCardRepo);
            })
            .Produces<List<SavePaymentMethodResponse>>(200)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<SavedCard> repo)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var spec = new SavedCardByBuyerIdSpec(request.BuyerId);
        var cards = await repo.ListAsync(spec);

        var result = cards.Select(c => new SavePaymentMethodResponse
        {
            PaymentMethodId = c.Id,
            LastFourDigits = c.LastFourDigits,
            CardBrand = c.CardBrand,
            Expiry = c.Expiry,
            CardType = c.CardType
        }).ToList();

        return Results.Ok(result);
    }
}
