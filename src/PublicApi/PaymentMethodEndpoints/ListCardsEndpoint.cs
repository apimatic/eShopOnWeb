using System.Linq;
using System.Security.Claims;
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

public class ListCardsRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListCardsEndpoint : IEndpoint<IResult, ListCardsRequest, IReadRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IReadRepository<SavedCard> cardRepo) =>
            {
                var request = new ListCardsRequest { BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty };
                return await HandleAsync(request, cardRepo);
            })
            .Produces<object>(200)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListCardsRequest request, IReadRepository<SavedCard> cardRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new SavedCardsByBuyerSpec(request.BuyerId);
        var cards = await cardRepo.ListAsync(spec);

        var result = cards.Select(c => new
        {
            paymentMethodId = c.Id,
            last4 = c.Last4,
            brand = c.Brand,
            expiry = c.Expiry,
            createdAt = c.CreatedAt
        });

        return Results.Ok(result);
    }
}
